namespace StockSharp.Connectors.Tests;

using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using static DemoTradingTestSupport;

/// <summary>
/// Credential-free smoke tests for the public market-data endpoints used to prepare
/// non-marketable orders in the simulated trading cycles.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public class DemoPublicEndpointTests
{
	[TestMethod]
	[Timeout(30000)]
	public async Task BinanceSpotTestnetOrderBook()
	{
		RequirePublicTests();
		using var client = Client("https://testnet.binance.vision");
		using var response = await SendJson(client, HttpMethod.Get,
			"/api/v3/depth?symbol=BTCUSDT&limit=5", string.Empty, [], "Binance Spot Testnet");

		AssertBook(response.RootElement.GetProperty("bids"), response.RootElement.GetProperty("asks"),
			"Binance Spot Testnet");
	}

	[TestMethod]
	[Timeout(30000)]
	public async Task BybitDemoOrderBook()
	{
		RequirePublicTests();
		using var client = Client("https://api-demo.bybit.com");
		using var response = await SendJson(client, HttpMethod.Get,
			"/v5/market/orderbook?category=spot&symbol=BTCUSDT&limit=5", string.Empty, [], "Bybit Demo");

		Assert.AreEqual(0, response.RootElement.GetProperty("retCode").GetInt32(), "Bybit Demo rejected the request.");
		var result = response.RootElement.GetProperty("result");
		Assert.AreEqual("BTCUSDT", result.GetProperty("s").GetString());
		AssertBook(result.GetProperty("b"), result.GetProperty("a"), "Bybit Demo");
	}

	[TestMethod]
	[Timeout(30000)]
	public async Task OkxPublicOrderBook()
	{
		RequirePublicTests();
		using var client = Client("https://www.okx.com");
		using var response = await SendJson(client, HttpMethod.Get,
			"/api/v5/market/books?instId=BTC-USDT&sz=5", string.Empty, [], "OKX Demo public market data");

		Assert.AreEqual("0", response.RootElement.GetProperty("code").GetString(), "OKX rejected the request.");
		var data = response.RootElement.GetProperty("data");
		Assert.IsTrue(data.GetArrayLength() > 0, "OKX returned no order books.");
		AssertBook(data[0].GetProperty("bids"), data[0].GetProperty("asks"), "OKX");
	}

	[TestMethod]
	[Timeout(30000)]
	public async Task BitmexTestnetOrderBook()
	{
		RequirePublicTests();
		using var client = Client("https://testnet.bitmex.com");
		using var response = await SendJson(client, HttpMethod.Get,
			"/api/v1/orderBook/L2?symbol=XBTUSD&depth=10", string.Empty, [], "BitMEX Testnet");

		var levels = response.RootElement.EnumerateArray().ToArray();
		Assert.IsTrue(levels.Any(l => l.GetProperty("side").GetString() == "Buy" && Decimal(l.GetProperty("price")) > 0),
			"BitMEX Testnet returned no valid bids.");
		Assert.IsTrue(levels.Any(l => l.GetProperty("side").GetString() == "Sell" && Decimal(l.GetProperty("price")) > 0),
			"BitMEX Testnet returned no valid asks.");
	}

	[TestMethod]
	[Timeout(30000)]
	public async Task DeribitTestnetOrderBook()
	{
		RequirePublicTests();
		using var client = Client("https://test.deribit.com");
		using var response = await SendJson(client, HttpMethod.Get,
			"/api/v2/public/get_order_book?instrument_name=BTC-PERPETUAL&depth=5", string.Empty, [], "Deribit Testnet");

		Assert.IsFalse(response.RootElement.TryGetProperty("error", out _), "Deribit Testnet rejected the request.");
		var result = response.RootElement.GetProperty("result");
		Assert.AreEqual("BTC-PERPETUAL", result.GetProperty("instrument_name").GetString());
		AssertBook(result.GetProperty("bids"), result.GetProperty("asks"), "Deribit Testnet");
	}

	[TestMethod]
	[Timeout(30000)]
	public async Task BitgetPublicOrderBook()
	{
		RequirePublicTests();
		using var client = Client("https://api.bitget.com");
		using var response = await SendJson(client, HttpMethod.Get,
			"/api/v2/spot/market/orderbook?symbol=BTCUSDT&limit=5", string.Empty, [], "Bitget Demo public market data");

		Assert.AreEqual("00000", response.RootElement.GetProperty("code").GetString(), "Bitget rejected the request.");
		var data = response.RootElement.GetProperty("data");
		AssertBook(data.GetProperty("bids"), data.GetProperty("asks"), "Bitget");
	}

	private static HttpClient Client(string baseAddress)
		=> new()
		{
			BaseAddress = new(baseAddress),
			Timeout = TimeSpan.FromSeconds(20),
		};

	private static void AssertBook(JsonElement bids, JsonElement asks, string exchange)
	{
		Assert.IsTrue(bids.GetArrayLength() > 0, $"{exchange} returned no bids.");
		Assert.IsTrue(asks.GetArrayLength() > 0, $"{exchange} returned no asks.");

		var bestBid = Decimal(bids[0][0]);
		var bestAsk = Decimal(asks[0][0]);
		Assert.IsTrue(bestBid > 0 && bestAsk >= bestBid, $"{exchange} returned an invalid order book.");
	}
}
