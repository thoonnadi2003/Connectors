namespace StockSharp.Connectors.Tests;

using System;
using System.Linq;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using RestSharp;

using StockSharp.Bitget.Native;

using static DemoTradingTestSupport;

[TestClass]
public class BitgetAuthenticationTests
{
	[TestMethod]
	public void SpotPostSignatureCoversSerializedBodyAndAddsDemoHeader()
	{
		const string key = "test-key";
		const string secret = "test-secret";
		const string passphrase = "test-passphrase";
		var url = new Uri("https://api.bitget.com/api/v2/spot/trade/place-order");
		var body = new { symbol = "BTCUSDT", side = "buy", price = "100" };
		using var authenticator = new Authenticator(true, key.Secure(), secret.Secure(), passphrase.Secure(), true);
		var request = new RestRequest((string)null, Method.Post);

		StockSharp.Bitget.Native.Spot.Extensions.ApplySecret(request, url, authenticator, body);

		var serializedBody = Parameter(request, ParameterType.RequestBody, null);
		var timestamp = Parameter(request, ParameterType.HttpHeader, "ACCESS-TIMESTAMP");
		var expected = HmacSha256Base64(secret, timestamp + "POST" + url.AbsolutePath + serializedBody);
		Assert.AreEqual(expected, Parameter(request, ParameterType.HttpHeader, "ACCESS-SIGN"));
		Assert.AreEqual("1", Parameter(request, ParameterType.HttpHeader, "paptrading"));
	}

	[TestMethod]
	public void FuturesGetSignatureUsesAmpersandSeparatedEscapedQuery()
	{
		const string secret = "test-secret";
		var url = new Uri("https://api.bitget.com/api/v2/mix/order/orders-pending");
		using var authenticator = new Authenticator(true, "test-key".Secure(), secret.Secure(), "pass".Secure(), true);
		var request = new RestRequest((string)null, Method.Get)
			.AddQueryParameter("productType", "USDT FUTURES")
			.AddQueryParameter("symbol", "BTC/USDT");

		StockSharp.Bitget.Native.Futures.Extensions.ApplySecret(request, url, authenticator);

		var timestamp = Parameter(request, ParameterType.HttpHeader, "ACCESS-TIMESTAMP");
		const string query = "productType=USDT%20FUTURES&symbol=BTC%2FUSDT";
		var expected = HmacSha256Base64(secret, timestamp + "GET" + url.AbsolutePath + "?" + query);
		Assert.AreEqual(expected, Parameter(request, ParameterType.HttpHeader, "ACCESS-SIGN"));
	}

	private static string Parameter(RestRequest request, ParameterType type, string name)
		=> request.Parameters.Single(p => p.Type == type && (name is null || p.Name == name)).Value?.ToString()!;
}
