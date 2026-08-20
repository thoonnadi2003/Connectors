namespace StockSharp.Connectors.Tests;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

internal static class DemoTradingTestSupport
{
	private static readonly JsonSerializerOptions _jsonOptions = new()
	{
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
	};

	public static void RequireLiveTests()
	{
		if (!string.Equals(Environment.GetEnvironmentVariable("STOCKSHARP_LIVE_TESTS"), "true", StringComparison.OrdinalIgnoreCase))
			Assert.Inconclusive("Set STOCKSHARP_LIVE_TESTS=true to run live integration tests.");
	}

	public static void RequirePublicTests()
	{
		if (!string.Equals(Environment.GetEnvironmentVariable("STOCKSHARP_PUBLIC_TESTS"), "true", StringComparison.OrdinalIgnoreCase))
			Assert.Inconclusive("Set STOCKSHARP_PUBLIC_TESTS=true to run public endpoint integration tests.");
	}

	public static string EnvironmentVariable(string name)
	{
		var value = Environment.GetEnvironmentVariable(name);

		if (string.IsNullOrWhiteSpace(value))
			Assert.Inconclusive($"Required environment variable {name} is not configured.");

		return value!;
	}

	public static string Json(object value) => JsonSerializer.Serialize(value, _jsonOptions);

	public static async Task<JsonDocument> SendJson(HttpClient client, HttpMethod method, string path,
		string body, IEnumerable<(string name, string value)> headers, string exchange)
	{
		using var request = new HttpRequestMessage(method, path);

		foreach (var (name, value) in headers)
			request.Headers.TryAddWithoutValidation(name, value);

		if (!string.IsNullOrEmpty(body))
			request.Content = new StringContent(body, Encoding.UTF8, "application/json");

		using var response = await client.SendAsync(request);
		var responseBody = await response.Content.ReadAsStringAsync();

		if (!response.IsSuccessStatusCode)
			Assert.Fail($"{exchange} request failed with HTTP {(int)response.StatusCode}.");

		try
		{
			return JsonDocument.Parse(responseBody);
		}
		catch (JsonException)
		{
			Assert.Fail($"{exchange} returned a non-JSON response.");
			throw;
		}
	}

	public static string HmacSha256Hex(string secret, string payload)
		=> Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();

	public static string HmacSha256Base64(string secret, string payload)
		=> Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payload)));

	public static string HmacSha384Hex(string secret, string payload)
		=> Convert.ToHexString(HMACSHA384.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();

	public static string HmacSha512Hex(string secret, string payload)
		=> Convert.ToHexString(HMACSHA512.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();

	public static string Sha512Hex(string payload)
		=> Convert.ToHexString(SHA512.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();

	public static decimal Decimal(JsonElement value)
		=> value.ValueKind == JsonValueKind.Number
			? value.GetDecimal()
			: decimal.Parse(value.GetString()!, CultureInfo.InvariantCulture);

	public static int Integer(JsonElement value)
		=> value.ValueKind == JsonValueKind.Number
			? value.GetInt32()
			: int.Parse(value.GetString()!, CultureInfo.InvariantCulture);

	public static string Format(decimal value) => value.ToString("0.############################", CultureInfo.InvariantCulture);
	public static decimal FloorToStep(decimal value, decimal step) => Math.Floor(value / step) * step;
	public static decimal CeilingToStep(decimal value, decimal step) => Math.Ceiling(value / step) * step;
	public static string ClientOrderId(string prefix) => prefix + Guid.NewGuid().ToString("N")[..20];
}
