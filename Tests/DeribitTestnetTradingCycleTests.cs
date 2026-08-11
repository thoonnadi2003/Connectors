namespace StockSharp.Connectors.Tests;

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using static DemoTradingTestSupport;

/// <summary>Opt-in Deribit Testnet order lifecycle test.</summary>
[TestClass]
[TestCategory("Integration")]
public class DeribitTestnetTradingCycleTests
{
	private const string _instrument = "BTC-PERPETUAL";

	[TestMethod]
	[Timeout(120000)]
	public async Task MarketDataAndTradingCycle()
	{
		RequireLiveTests();
		var clientId = EnvironmentVariable("DERIBIT_TESTNET_CLIENT_ID");
		var clientSecret = EnvironmentVariable("DERIBIT_TESTNET_CLIENT_SECRET");

		using var client = new HttpClient { BaseAddress = new("https://test.deribit.com") };
		using var instrumentResponse = await SendRpc(client, "public/get_instrument",
			new { instrument_name = _instrument });
		var instrument = Result(instrumentResponse.RootElement);
		using var bookResponse = await SendRpc(client, "public/get_order_book",
			new { instrument_name = _instrument, depth = 5 });
		var book = Result(bookResponse.RootElement);
		var bestBid = Decimal(book.GetProperty("best_bid_price"));
		var bestAsk = Decimal(book.GetProperty("best_ask_price"));
		Assert.IsTrue(bestBid > 0 && bestAsk >= bestBid, "Invalid Deribit Testnet order book.");

		using var authenticated = await SendRpc(client, "public/auth", new
		{
			grant_type = "client_credentials",
			client_id = clientId,
			client_secret = clientSecret,
		});
		var accessToken = Result(authenticated.RootElement).GetProperty("access_token").GetString();
		Assert.IsFalse(string.IsNullOrWhiteSpace(accessToken), "Deribit Testnet authentication failed.");

		using var account = await SendRpc(client, "private/get_account_summary", new { currency = "BTC" }, accessToken);
		Assert.AreEqual("BTC", Result(account.RootElement).GetProperty("currency").GetString());

		var tickSize = Decimal(instrument.GetProperty("tick_size"));
		var minAmount = Decimal(instrument.GetProperty("min_trade_amount"));
		var price = FloorToStep(bestBid * 0.8m, tickSize);
		var label = ClientOrderId("ss-der-");
		string orderId = null;

		try
		{
			using var created = await SendRpc(client, "private/buy", new
			{
				instrument_name = _instrument,
				amount = minAmount,
				type = "limit",
				price,
				time_in_force = "good_til_cancelled",
				post_only = true,
				label,
			}, accessToken);
			orderId = Result(created.RootElement).GetProperty("order").GetProperty("order_id").GetString();
			Assert.IsFalse(string.IsNullOrWhiteSpace(orderId), "Deribit Testnet did not return an order id.");

			using var queried = await SendRpc(client, "private/get_order_state", new { order_id = orderId }, accessToken);
			Assert.AreEqual(orderId, Result(queried.RootElement).GetProperty("order_id").GetString());
		}
		finally
		{
			if (!string.IsNullOrWhiteSpace(orderId))
			{
				using var canceled = await SendRpc(client, "private/cancel", new { order_id = orderId }, accessToken);
				Assert.AreEqual(orderId, Result(canceled.RootElement).GetProperty("order_id").GetString());
			}
		}
	}

	private static async Task<JsonDocument> SendRpc(HttpClient client, string method, object parameters, string accessToken = null)
	{
		var body = Json(new { jsonrpc = "2.0", id = 1, method, @params = parameters });
		var headers = new List<(string, string)>();
		if (!string.IsNullOrWhiteSpace(accessToken))
			headers.Add(("Authorization", "Bearer " + accessToken));
		return await SendJson(client, HttpMethod.Post, "/api/v2/" + method, body, headers, "Deribit Testnet");
	}

	private static JsonElement Result(JsonElement root)
	{
		if (root.TryGetProperty("error", out var error))
			Assert.Fail($"Deribit Testnet rejected the request with code {error.GetProperty("code").GetInt32()}.");
		return root.GetProperty("result");
	}
}
