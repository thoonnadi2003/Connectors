namespace StockSharp.Connectors.Tests;

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using static DemoTradingTestSupport;

/// <summary>Opt-in authentication check for the current FXCM Demo IAM flow.</summary>
[TestClass]
[TestCategory("Integration")]
public class FxcmDemoAuthenticationTests
{
	private static readonly Uri _baseAddress = new("https://endpoints-demo.fxcm.com");

	[TestMethod]
	[Timeout(120000)]
	public async Task LoginReturnsShortLivedTokens()
	{
		RequireLiveTests();
		var login = EnvironmentVariable("FXCM_DEMO_LOGIN");
		var password = EnvironmentVariable("FXCM_DEMO_PASSWORD");

		var cookies = new CookieContainer();
		using var handler = new HttpClientHandler
		{
			CookieContainer = cookies,
			UseCookies = true,
		};
		using var client = new HttpClient(handler) { BaseAddress = _baseAddress };
		var cookieDomainHeader = new List<(string, string)> { ("X-COOKIE-DOMAIN", "fxcm.com") };

		using var systems = await SendJson(client, HttpMethod.Get,
			"/iam/trading-systems/" + Uri.EscapeDataString(login), string.Empty,
			cookieDomainHeader, "FXCM Demo IAM");

		Assert.AreEqual(JsonValueKind.Array, systems.RootElement.ValueKind,
			"FXCM Demo IAM returned an unexpected trading-systems payload.");
		Assert.IsTrue(systems.RootElement.GetArrayLength() > 0,
			"FXCM Demo IAM returned no trading systems for this login.");

		var tradingSystem = systems.RootElement[0];
		var tradingSessionId = RequiredString(tradingSystem, "tradingSessionId");
		var tradingSessionSubId = RequiredString(tradingSystem, "tradingSessionSubId");
		var xsrfToken = cookies.GetCookies(_baseAddress)["XSRF-TOKEN"]?.Value;
		Assert.IsFalse(string.IsNullOrWhiteSpace(xsrfToken),
			"FXCM Demo IAM did not set the required XSRF cookie.");

		var authenticationHeaders = new List<(string, string)>
		{
			("X-COOKIE-DOMAIN", "fxcm.com"),
			("X-XSRF-TOKEN", xsrfToken!),
		};
		var authenticationBody = Json(new
		{
			loginId = login,
			password,
			tradingSessionId,
			tradingSessionSubId,
			appName = "StockSharp Connector Tests",
		});

		using var authenticated = await SendJson(client, HttpMethod.Post, "/iam/authenticate/",
			authenticationBody, authenticationHeaders, "FXCM Demo IAM");
		_ = RequiredString(authenticated.RootElement, "accessToken");
		_ = RequiredString(authenticated.RootElement, "refreshToken");
	}

	private static string RequiredString(JsonElement element, string propertyName)
	{
		Assert.IsTrue(element.TryGetProperty(propertyName, out var property),
			$"FXCM Demo IAM response is missing {propertyName}.");
		var value = property.GetString();
		Assert.IsFalse(string.IsNullOrWhiteSpace(value),
			$"FXCM Demo IAM returned an empty {propertyName}.");
		return value!;
	}
}
