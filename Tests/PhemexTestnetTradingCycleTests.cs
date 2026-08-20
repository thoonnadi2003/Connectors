namespace StockSharp.Connectors.Tests;

using System;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Messages;
using StockSharp.Phemex;

/// <summary>
/// Opt-in Phemex Testnet smoke test covering authenticated WebSocket login and the
/// REST-backed futures order lifecycle. Credentials are read only from environment variables.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public class PhemexTestnetTradingCycleTests
{
	private const string _restEndpoint = "https://testnet-api.phemex.com";
	private const string _webSocketEndpoint = "wss://testnet-api.phemex.com/ws";
	private const string _symbol = "BTCUSDT";

	[TestMethod]
	[Timeout(120000)]
	public async Task ConnectorAuthenticationAndTradingCycle()
	{
		DemoTradingTestSupport.RequireLiveTests();
		var key = DemoTradingTestSupport.EnvironmentVariable("PHEMEX_TESTNET_API_KEY").Trim();
		var secret = DemoTradingTestSupport.EnvironmentVariable("PHEMEX_TESTNET_API_SECRET").Trim();

		using var client = new HttpClient { BaseAddress = new(_restEndpoint) };
		using var depth = await DemoTradingTestSupport.SendJson(client, HttpMethod.Get,
			$"/md/v2/orderbook?symbol={_symbol}", string.Empty, [], "Phemex Testnet");
		var root = depth.RootElement;
		Assert.AreEqual(JsonValueKind.Null, root.GetProperty("error").ValueKind,
			"Phemex Testnet rejected the public order-book request.");
		var bestBid = DemoTradingTestSupport.Decimal(root.GetProperty("result")
			.GetProperty("orderbook_p").GetProperty("bids")[0][0]);
		Assert.IsTrue(bestBid > 0m, "Phemex Testnet returned an invalid best bid.");

		// Keep the order well below the spread so the test validates cancellation without
		// intentionally executing a trade. BTCUSDT accepts 0.1 price ticks and 0.001 quantity.
		var price = Math.Floor(bestBid * 0.8m * 10m) / 10m;
		const decimal quantity = 0.001m;

		var adapter = new PhemexMessageAdapter(new MillisecondIncrementalIdGenerator())
		{
			Key = key.Secure(),
			Secret = secret.Secure(),
			Sections = [PhemexSections.Futures],
			RestEndpoint = _restEndpoint,
			PublicWsEndpoint = _webSocketEndpoint,
			PrivateWsEndpoint = _webSocketEndpoint,
		};

		await using var harness = new MarketDataTestHarness(adapter);
		using var testSource = new CancellationTokenSource(TimeSpan.FromSeconds(105));
		var securityId = new SecurityId
		{
			SecurityCode = _symbol,
			BoardCode = BoardCodes.PhemexFutures,
		};
		var registerId = adapter.TransactionIdGenerator.GetNextId();
		var orderCondition = new PhemexOrderCondition
		{
			// Testnet accounts can use Hedge Mode. In that mode Phemex rejects the
			// one-way "Merged" side with TE_ERR_INCONSISTENT_POS_MODE.
			PositionSide = PhemexPositionSides.Long,
		};
		string orderId = null;
		var canceled = false;

		try
		{
			// Connect authenticates the private Phemex Testnet WebSocket before completing.
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
				Condition = orderCondition,
			}, testSource.Token);

			var registered = await WaitForExecutionAsync(registeredReader, registerId,
				TimeSpan.FromSeconds(30), testSource.Token);
			Assert.IsFalse(registered.OrderStringId.IsEmpty(),
				"Phemex Testnet did not return an order id.");
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
				Condition = orderCondition,
			}, testSource.Token);

			var cancellation = await WaitForExecutionAsync(canceledReader, cancelId,
				TimeSpan.FromSeconds(30), testSource.Token);
			Assert.AreEqual(OrderStates.Done, cancellation.OrderState,
				$"Unexpected canceled order state: {cancellation.OrderState}.");
			canceled = true;
			Assert.AreEqual(0, harness.Errors.Length,
				"Phemex adapter reported an out-of-band error during the trading cycle.");
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
						Condition = orderCondition,
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
