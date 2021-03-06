// --------------------------------------------------------------------------------------------------------------------
// <copyright file="SmartOrderRoutingEngine.cs" company="LunchBox corp">
//     Copyright 2014 The Lunch-Box mob: 
//           Ozgur DEVELIOGLU (@Zgurrr)
//           Cyrille  DUPUYDAUBY (@Cyrdup)
//           Tomasz JASKULA (@tjaskula)
//           Mendel MONTEIRO-BECKERMAN (@MendelMonteiro)
//           Thomas PIERRAIN (@tpierrain)
//     
//     Licensed under the Apache License, Version 2.0 (the "License");
//     you may not use this file except in compliance with the License.
//     You may obtain a copy of the License at
//         http://www.apache.org/licenses/LICENSE-2.0
//     Unless required by applicable law or agreed to in writing, software
//     distributed under the License is distributed on an "AS IS" BASIS,
//     WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//     See the License for the specific language governing permissions and
//     limitations under the License.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------
namespace SimpleOrderRouting
{
    using System;
    using System.Collections.Generic;

    using SimpleOrderRouting.Investors;
    using SimpleOrderRouting.Markets;
    using SimpleOrderRouting.Markets.Orders;
    using SimpleOrderRouting.SolvingStrategies;

    /// <summary>
    /// Provides access to the various services offered by the external market venues.
    /// Manages incoming InvestorInstructions and monitor their lifecycle.
    /// Is responsible for the consistency of the open positions (i.e. alive orders) that are present on every markets.
    /// </summary>
    public class SmartOrderRoutingEngine : IHandleInvestorInstructions
    {
        private readonly ICanRouteOrders routeOrders;
        private readonly MarketSnapshotProvider marketSnapshotProvider;

        /// <summary>
        /// Initializes a new instance of the <see cref="SmartOrderRoutingEngine"/> class.
        /// </summary>
        /// <param name="marketsProvider">The markets provider.</param>
        /// <param name="routeOrders">The order routing.</param>
        /// <param name="marketDataProvider">The market data provider.</param>
        public SmartOrderRoutingEngine(IProvideMarkets marketsProvider, ICanRouteOrders routeOrders, ICanReceiveMarketData marketDataProvider)
        {
            this.routeOrders = routeOrders;
            var availableMarkets = marketsProvider.GetAvailableMarketNames();
            this.marketSnapshotProvider = new MarketSnapshotProvider(availableMarkets, marketDataProvider);
        }

        public void Route(InvestorInstruction investorInstruction, Action<InvestorInstructionExecutedEventArgs> instructionExecutedCallback, Action<string> failureCallback)
        {
            // Prepares to feedback the investor
            var instructionExecutionContext = new InstructionExecutionContext(investorInstruction, instructionExecutedCallback, failureCallback);

            this.routeOrders.OrderExecuted += this.WhenOneOrderIsExecuted(instructionExecutionContext);
            this.routeOrders.OrderFailed += this.WhenOneOrderFailed(instructionExecutionContext);

            this.RouteImpl(instructionExecutionContext);

            this.routeOrders.OrderExecuted -= this.WhenOneOrderIsExecuted(instructionExecutionContext);
            this.routeOrders.OrderFailed -= this.WhenOneOrderFailed(instructionExecutionContext);
        }

        private void RouteImpl(InstructionExecutionContext instructionExecutionContext)
        {
            // 1. Prepare the corresponding OrderBasket (via solver)
            var solver = new MarketSweepSolver(this.marketSnapshotProvider);
            var orderBasket = solver.Solve(instructionExecutionContext, this.routeOrders);

            // 2. Route the OrderBasket
            this.routeOrders.Route(orderBasket);
        }

        private EventHandler<DealExecutedEventArgs> WhenOneOrderIsExecuted(InstructionExecutionContext instructionExecutionContext)
        {
            // TODO: must process the message only if it's related to the proper instruction
            return (sender, dealExecuted) => instructionExecutionContext.RecordOrderExecution(dealExecuted.Quantity);
        }

        private EventHandler<OrderFailedEventArgs> WhenOneOrderFailed(InstructionExecutionContext instructionExecutionContext)
        {
            // TODO: must process the message only if it's related to the proper instruction
            return (sender, orderFailed) => this.OnOrderFailed(orderFailed, instructionExecutionContext);
        }

        private void OnOrderFailed(OrderFailedEventArgs reason, InstructionExecutionContext instructionExecutionContext)
        {
            if (instructionExecutionContext.ShouldTheInstructionBeContinued())
            {
                this.RetryInvestorInstruction(instructionExecutionContext);
            }
            else
            {
                instructionExecutionContext.DeclareFailure(reason);
            }
        }

        private void RetryInvestorInstruction(InstructionExecutionContext instructionExecutionContext)
        {
            this.RouteImpl(instructionExecutionContext);
        }    
    }
}