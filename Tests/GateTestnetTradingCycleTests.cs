namespace StockSharp.Connectors.Tests;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using static DemoTradingTestSupport;

/// <summary>Opt-in Gate futures Testnet order lifecycle test.</summary>
[TestClass]
[TestCategory("Integration")]
public class GateTestnetTradingCycleTests
{
	private const string _contract = "BTC_USDT";
	private const string _prefix = "/api/v4";

	[TestMethod]
	[Timeout(120000)]
	public async Task MarketDataAndTradingCycle()
	{
		RequireLiveTests();
		var key = EnvironmentVariable("GATE_TESTNET_API_KEY");
		var secret = EnvironmentVariable("GATE_TESTNET_API_SECRET");
		using var client = new HttpClient { BaseAddress = new("https://api-testnet.gateapi.io") };

		using var contractResponse = await SendJson(client, HttpMethod.Get,
			$"{_prefix}/futures/usdt/contracts/{_contract}", string.Empty, [], "Gate Testnet");
		var contract = contractResponse.RootElement;
		using var bookResponse = await SendJson(client, HttpMethod.Get,
			$"{_prefix}/futures/usdt/order_book?contract={_contract}&limit=5", string.Empty, [], "Gate Testnet");
		var book = bookResponse.RootElement;
		var bestBid = Decimal(book.GetProperty("bids")[0].GetProperty("p"));
		var bestAsk = Decimal(book.GetProperty("asks")[0].GetProperty("p"));
		Assert.IsTrue(bestBid > 0 && bestAsk >= bestBid, "Invalid Gate Testnet order book.");

		using var account = await SendSigned(client, HttpMethod.Get,
			$"{_prefix}/futures/usdt/accounts", string.Empty, key, secret);
		Assert.IsTrue(account.RootElement.TryGetProperty("currency", out _), "Gate Testnet account response changed.");

		var orderSizeMin = contract.TryGetProperty("order_size_min", out var minimum)
			? Math.Max(1L, minimum.GetInt64()) : 1L;
		var orderPriceRound = Decimal(contract.GetProperty("order_price_round"));
		var price = FloorToStep(bestBid * 0.8m, orderPriceRound);
		var body = Json(new
		{
			contract = _contract,
			size = orderSizeMin,
			price = Format(price),
			tif = "gtc",
			text = "t-" + ClientOrderId("ssgate"),
		});
		string orderId = null;

		try
		{
			using var created = await SendSigned(client, HttpMethod.Post,
				$"{_prefix}/futures/usdt/orders", body, key, secret);
			orderId = created.RootElement.GetProperty("id").ToString();
			Assert.IsFalse(string.IsNullOrWhiteSpace(orderId), "Gate Testnet did not return an order id.");

			using var queried = await SendSigned(client, HttpMethod.Get,
				$"{_prefix}/futures/usdt/orders/{Uri.EscapeDataString(orderId)}", string.Empty, key, secret);
			Assert.AreEqual(orderId, queried.RootElement.GetProperty("id").ToString());
		}
		finally
		{
			if (!string.IsNullOrWhiteSpace(orderId))
			{
				using var canceled = await SendSigned(client, HttpMethod.Delete,
					$"{_prefix}/futures/usdt/orders/{Uri.EscapeDataString(orderId)}", string.Empty, key, secret);
				Assert.AreEqual(orderId, canceled.RootElement.GetProperty("id").ToString());
			}
		}
	}

	private static async Task<JsonDocument> SendSigned(HttpClient client, HttpMethod method, string path,
		string body, string key, string secret)
	{
		var uri = new Uri(path, UriKind.RelativeOrAbsolute);
		var pathOnly = uri.IsAbsoluteUri ? uri.AbsolutePath : path.Split('?')[0];
		var query = path.Contains('?') ? path[(path.IndexOf('?') + 1)..] : string.Empty;
		var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
		var payload = $"{method.Method.ToUpperInvariant()}\n{pathOnly}\n{query}\n{Sha512Hex(body)}\n{timestamp}";
		var headers = new List<(string, string)>
		{
			("KEY", key), ("Timestamp", timestamp), ("SIGN", HmacSha512Hex(secret, payload)),
		};
		return await SendJson(client, method, path, body, headers, "Gate Testnet");
	}
}
