namespace StockSharp.Connectors.Tests;

using System;
using System.IO;
using System.Net.WebSockets;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Binance;
using StockSharp.Binance.Native;
using StockSharp.Messages;

[TestClass]
public class BinanceRegressionTests
{
	[TestMethod]
	public void DemoEndpointsMatchSpotTestnetServices()
	{
		using var adapter = new BinanceMessageAdapter(new IncrementalIdGenerator());

		Assert.AreEqual("testnet.binance.vision", adapter.DemoHostRestSpot);
		Assert.AreEqual("stream.testnet.binance.vision", adapter.DemoHostWebSocketSpot);
		Assert.AreEqual("wss://ws-api.testnet.binance.vision/ws-api/v3",
			PusherClient.GetSpotAccountWebSocketUrl(true));
		Assert.AreEqual("wss://ws-api.binance.com:443/ws-api/v3",
			PusherClient.GetSpotAccountWebSocketUrl(false));
	}

	[TestMethod]
	public void SocketInterruptionsAreRecoverableButApplicationErrorsAreNot()
	{
		Assert.IsTrue(PusherClient.IsTransientDisconnect(new WebSocketException("socket interrupted")));
		Assert.IsTrue(PusherClient.IsTransientDisconnect(new IOException("stream interrupted")));
		Assert.IsTrue(PusherClient.IsTransientDisconnect(
			new InvalidOperationException("wrapper", new IOException("stream interrupted"))));
		Assert.IsFalse(PusherClient.IsTransientDisconnect(new InvalidOperationException("bad payload")));
	}

	[TestMethod]
	public void SpotOrderDoesNotRequirePortfolioName()
	{
		Assert.IsNull(BinanceMessageAdapter.TryGetSymbolFromIsolatedPortfolioName(null));
		Assert.IsNull(BinanceMessageAdapter.TryGetSymbolFromIsolatedPortfolioName(string.Empty));
	}

	[TestMethod]
	public void SpotPostOnlyDoesNotSendTimeInForce()
	{
		var type = ((OrderTypes?)OrderTypes.Limit).ToNative(BinanceSections.Spot, 1m, null, true, out var isTif);

		Assert.AreEqual("LIMIT_MAKER", type);
		Assert.IsFalse(isTif);

		type = ((OrderTypes?)OrderTypes.Limit).ToNative(BinanceSections.Spot, 1m, null, false, out isTif);
		Assert.AreEqual("LIMIT", type);
		Assert.IsTrue(isTif);
	}
}
