namespace StockSharp.Connectors.Tests;

using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.BloFin;
using StockSharp.Messages;

/// <summary>
/// Opt-in BloFin Demo Trading smoke test covering private WebSocket authentication and
/// the futures order lifecycle. Credentials are read only from environment variables.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public class BloFinDemoTradingCycleTests
{
	private const string _restEndpoint = "https://demo-trading-openapi.blofin.com";
	private const string _symbol = "BTC-USDT";

	[TestMethod]
	[Timeout(120000)]
	public async Task ConnectorAuthenticationAndTradingCycle()
	{
		DemoTradingTestSupport.RequireLiveTests();
		var key = DemoTradingTestSupport.EnvironmentVariable("BLOFIN_DEMO_API_KEY").Trim();
		var secret = DemoTradingTestSupport.EnvironmentVariable("BLOFIN_DEMO_API_SECRET").Trim();
		var passphrase = DemoTradingTestSupport.EnvironmentVariable("BLOFIN_DEMO_API_PASSPHRASE").Trim();

		var (price, quantity) = await GetSafeOrderAsync();
		var adapter = new BloFinMessageAdapter(new MillisecondIncrementalIdGenerator())
		{
			Key = key.Secure(),
			Secret = secret.Secure(),
			Passphrase = passphrase.Secure(),
			IsDemo = true,
		};

		await using var harness = new MarketDataTestHarness(adapter);
		using var testSource = new CancellationTokenSource(TimeSpan.FromSeconds(105));
		var securityId = new SecurityId
		{
			SecurityCode = _symbol,
			BoardCode = BoardCodes.BloFin,
		};
		var registerId = adapter.TransactionIdGenerator.GetNextId();
		string orderId = null;
		var canceled = false;

		try
		{
			// BloFin completes Connect only after its private Demo WebSocket login succeeds.
			await harness.ConnectAsync(TimeSpan.FromSeconds(35), testSource.Token);

			var registeredReader = harness.CreateReader();
			harness.Post(new OrderRegisterMessage
			{
				TransactionId = registerId,
				SecurityId = securityId,
				Side = Sides.Buy,
				Price = price,
				Volume = quantity,
				OrderType = OrderTypes.Limit,
				TimeInForce = TimeInForce.PutInQueue,
				PostOnly = true,
			}, testSource.Token);

			var registered = await WaitForExecutionAsync(registeredReader, registerId,
				TimeSpan.FromSeconds(30), testSource.Token);
			Assert.IsFalse(registered.OrderStringId.IsEmpty(),
				"BloFin Demo did not return an order id.");
			Assert.IsTrue(registered.OrderState is OrderStates.Pending or OrderStates.Active,
				$"Unexpected registered order state: {registered.OrderState}.");
			orderId = registered.OrderStringId;

			var canceledReader = harness.CreateReader();
			var cancelId = adapter.TransactionIdGenerator.GetNextId();
			harness.Post(new OrderCancelMessage
			{
				TransactionId = cancelId,
				OriginalTransactionId = registerId,
				SecurityId = securityId,
				OrderStringId = orderId,
			}, testSource.Token);

			var cancellation = await WaitForExecutionAsync(canceledReader, cancelId,
				TimeSpan.FromSeconds(30), testSource.Token);
			Assert.AreEqual(OrderStates.Done, cancellation.OrderState,
				$"Unexpected canceled order state: {cancellation.OrderState}.");
			canceled = true;
			Assert.AreEqual(0, harness.Errors.Length,
				"BloFin adapter reported an out-of-band error during the trading cycle.");
		}
		finally
		{
			if (!canceled && !orderId.IsEmpty())
			{
				try
				{
					harness.Post(new OrderCancelMessage
					{
						TransactionId = adapter.TransactionIdGenerator.GetNextId(),
						OriginalTransactionId = registerId,
						SecurityId = securityId,
						OrderStringId = orderId,
					}, CancellationToken.None);
					await Task.Delay(TimeSpan.FromSeconds(2), CancellationToken.None);
				}
				catch (Exception)
				{
				}
			}

			try
			{
				await harness.DisconnectAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
			}
			catch (Exception)
			{
			}
		}
	}

	private static async Task<(decimal price, decimal quantity)> GetSafeOrderAsync()
	{
		using var client = new HttpClient { BaseAddress = new(_restEndpoint) };
		client.DefaultRequestHeaders.UserAgent.ParseAdd("StockSharp-DemoTradingTests/1.0");

		using var instrumentResponse = await client.GetAsync(
			$"/api/v1/market/instruments?instId={_symbol}");
		if (instrumentResponse.StatusCode == HttpStatusCode.Forbidden)
			Assert.Inconclusive("BloFin Demo blocks this runner with HTTP 403; retry from an allowed region/network.");
		Assert.IsTrue(instrumentResponse.IsSuccessStatusCode,
			$"BloFin Demo instruments request failed with HTTP {(int)instrumentResponse.StatusCode}.");
		using var instruments = JsonDocument.Parse(await instrumentResponse.Content.ReadAsStringAsync());
		Assert.AreEqual("0", instruments.RootElement.GetProperty("code").GetString(),
			"BloFin Demo rejected the instruments request.");
		var instrument = instruments.RootElement.GetProperty("data")[0];
		var tickSize = DemoTradingTestSupport.Decimal(instrument.GetProperty("tickSize"));
		var minimumSize = DemoTradingTestSupport.Decimal(instrument.GetProperty("minSize"));
		var lotSize = DemoTradingTestSupport.Decimal(instrument.GetProperty("lotSize"));

		using var bookResponse = await client.GetAsync(
			$"/api/v1/market/books?instId={_symbol}&size=5");
		if (bookResponse.StatusCode == HttpStatusCode.Forbidden)
			Assert.Inconclusive("BloFin Demo blocks this runner with HTTP 403; retry from an allowed region/network.");
		Assert.IsTrue(bookResponse.IsSuccessStatusCode,
			$"BloFin Demo order-book request failed with HTTP {(int)bookResponse.StatusCode}.");
		using var book = JsonDocument.Parse(await bookResponse.Content.ReadAsStringAsync());
		Assert.AreEqual("0", book.RootElement.GetProperty("code").GetString(),
			"BloFin Demo rejected the order-book request.");
		var bestBid = DemoTradingTestSupport.Decimal(book.RootElement.GetProperty("data")[0]
			.GetProperty("bids")[0][0]);
		Assert.IsTrue(bestBid > 0m, "BloFin Demo returned an invalid best bid.");

		// A post-only bid below the spread avoids intentionally executing the demo order.
		var price = DemoTradingTestSupport.FloorToStep(bestBid * 0.9m, tickSize);
		var quantity = DemoTradingTestSupport.CeilingToStep(minimumSize, lotSize);
		return (price, quantity);
	}

	private static async Task<ExecutionMessage> WaitForExecutionAsync(MessageReader reader,
		long transactionId, TimeSpan timeout, CancellationToken cancellationToken)
	{
		var message = await reader.WaitAsync<Message>(
			m => m is ErrorMessage || m is ExecutionMessage
			{
				HasOrderInfo: true,
				OriginalTransactionId: var original,
			} && original == transactionId,
			timeout, cancellationToken);

		if (message is ErrorMessage error)
			Assert.Fail(error.Error.ToString());
		var execution = (ExecutionMessage)message;
		if (execution.Error is not null)
			Assert.Fail(execution.Error.ToString());
		return execution;
	}
}
