namespace StockSharp.Connectors.Tests;

using System;
using System.Threading;
using System.Threading.Tasks;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Deriv;
using StockSharp.Messages;

/// <summary>
/// Opt-in Deriv demo test covering authenticated connection, proposal, purchase, and
/// natural completion of a short-lived synthetic-index contract.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public class DerivDemoTradingCycleTests
{
	private const string _symbol = "R_100";

	[TestMethod]
	[Timeout(150000)]
	public async Task ConnectorAuthenticationAndTradingCycle()
	{
		DemoTradingTestSupport.RequireLiveTests();
		var appId = DemoTradingTestSupport.EnvironmentVariable("DERIV_DEMO_APP_ID").Trim();
		var token = DemoTradingTestSupport.EnvironmentVariable("DERIV_DEMO_API_TOKEN").Trim();

		var adapter = new DerivMessageAdapter(new MillisecondIncrementalIdGenerator())
		{
			AppId = appId,
			Token = token.Secure(),
			IsDemo = true,
		};
		await using var harness = new MarketDataTestHarness(adapter);
		using var testSource = new CancellationTokenSource(TimeSpan.FromSeconds(135));
		var securityId = new SecurityId
		{
			SecurityCode = _symbol,
			BoardCode = BoardCodes.Deriv,
		};
		var registerId = adapter.TransactionIdGenerator.GetNextId();

		try
		{
			// Connect resolves and validates an active demo account before opening the
			// authenticated WebSocket session.
			await harness.ConnectAsync(TimeSpan.FromSeconds(40), testSource.Token);

			var reader = harness.CreateReader();
			harness.Post(new OrderRegisterMessage
			{
				TransactionId = registerId,
				SecurityId = securityId,
				Side = Sides.Buy,
				Volume = 1m,
				OrderType = OrderTypes.Market,
				Condition = new DerivOrderCondition
				{
					ContractType = DerivContractTypes.Call,
					Basis = DerivBasisTypes.Stake,
					Currency = "USD",
					Duration = 5,
					DurationUnit = DerivDurationUnits.Ticks,
				},
			}, testSource.Token);

			var active = await WaitForOrderAsync(reader, registerId,
				message => message.OrderState == OrderStates.Active,
				TimeSpan.FromSeconds(35), testSource.Token);
			Assert.IsTrue(active.OrderId > 0, "Deriv did not return a contract id.");

			var completed = await WaitForOrderAsync(reader, registerId,
				message => message.OrderId == active.OrderId &&
					message.OrderState is OrderStates.Done or OrderStates.Failed,
				TimeSpan.FromSeconds(60), testSource.Token);
			Assert.AreEqual(OrderStates.Done, completed.OrderState,
				$"Deriv demo contract ended in state {completed.OrderState}.");
			Assert.AreEqual(0m, completed.Balance,
				"Completed Deriv contract must have no remaining balance.");
			Assert.AreEqual(0, harness.Errors.Length,
				"Deriv adapter reported an out-of-band error during the demo cycle.");
		}
		finally
		{
			try
			{
				await harness.DisconnectAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
			}
			catch (Exception)
			{
			}
		}
	}

	private static async Task<ExecutionMessage> WaitForOrderAsync(MessageReader reader,
		long transactionId, Func<ExecutionMessage, bool> condition, TimeSpan timeout,
		CancellationToken cancellationToken)
	{
		var message = await reader.WaitAsync<Message>(
			item => item is ErrorMessage || item is ExecutionMessage execution &&
				execution.HasOrderInfo &&
				execution.OriginalTransactionId == transactionId && condition(execution),
			timeout, cancellationToken);

		if (message is ErrorMessage error)
			Assert.Fail(error.Error.ToString());
		var execution = (ExecutionMessage)message;
		if (execution.Error is not null)
			Assert.Fail(execution.Error.ToString());
		return execution;
	}
}
