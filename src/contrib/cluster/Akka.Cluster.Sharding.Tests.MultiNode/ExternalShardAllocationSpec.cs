﻿//-----------------------------------------------------------------------
// <copyright file="ExternalShardAllocationSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Cluster.Sharding.External;
using Akka.Event;
using Akka.MultiNode.TestAdapter;
using Akka.Remote.TestKit;
using Akka.TestKit.TestActors;
using Akka.Util;
using FluentAssertions;

namespace Akka.Cluster.Sharding.Tests
{
    public class ExternalShardAllocationSpecConfig : MultiNodeClusterShardingConfig
    {
        public RoleName First { get; }
        public RoleName Second { get; }
        public RoleName Third { get; }
        public RoleName Forth { get; }

        public ExternalShardAllocationSpecConfig()
            : base(loglevel: "DEBUG", additionalConfig: @"
            akka.cluster.sharding {
                retry-interval = 2000ms
                waiting-for-state-timeout = 2000ms
                rebalance-interval = 1s
            }
            akka.cluster.sharding.distributed-data.durable.keys = []
            akka.testconductor.barrier-timeout = 60s
            ")
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");
            Forth = Role("forth");
        }
    }

    public class ExternalShardAllocationSpec : MultiNodeClusterShardingSpec<ExternalShardAllocationSpecConfig>
    {
        #region setup

        private class GiveMeYourHome : ActorBase
        {
            public class Get
            {
                public Get(string id)
                {
                    Id = id;
                }

                public string Id { get; }
            }
            public class Home
            {
                public Home(Address address)
                {
                    Address = address;
                }

                public Address Address { get; }
            }

            public sealed class MessageExtractor: IMessageExtractor
            {
                // shard == id to make testing easier
                public string EntityId(object message)
                    => message switch
                    {
                        Get g => g.Id,
                        _ => null
                    };

                public object EntityMessage(object message)
                    => message;

                public string ShardId(object message)
                    => message switch
                    {
                        Get g => g.Id,
                        _ => null
                    };

                public string ShardId(string entityId, object messageHint = null)
                    => entityId;
            }

            private readonly Address selfAddress = Cluster.Get(Context.System).SelfAddress;
            private readonly ILoggingAdapter log = Context.GetLogger();

            public GiveMeYourHome()
            {
                log.Info("Started on {0}", selfAddress);
            }

            protected override bool Receive(object message)
            {
                switch (message)
                {
                    case Get _:
                        Sender.Tell(new Home(selfAddress));
                        return true;
                }
                return false;
            }
        }

        private const string TypeName = "home";
        private const string InitiallyOnForth = "on-forth";

        private readonly Lazy<IActorRef> _shardRegion;

        public ExternalShardAllocationSpec()
            : this(new ExternalShardAllocationSpecConfig(), typeof(ExternalShardAllocationSpec))
        {

        }

        protected ExternalShardAllocationSpec(ExternalShardAllocationSpecConfig config, Type type)
            : base(config, type)
        {
            _shardRegion = new Lazy<IActorRef>(() => StartSharding(
                Sys,
                  typeName: TypeName,
                  entityProps: Props.Create(() => new GiveMeYourHome()),
                  messageExtractor: new GiveMeYourHome.MessageExtractor(),
                  allocationStrategy: new ExternalShardAllocationStrategy(Sys, TypeName))
                );
        }

        #endregion

        [MultiNodeFact]
        public void External_shard_allocation_specs()
        {
            External_shard_allocation_must_form_cluster();
            External_shard_allocation_must_start_cluster_sharding();
            External_shard_allocation_must_default_to_allocating_a_shard_to_the_local_shard_region();
            External_shard_allocation_must_move_shard_via_distributed_data();
            External_shard_allocation_must_allocate_to_a_node_that_does_not_exist_yet();
            External_shard_allocation_must_move_allocation();
        }

        private void External_shard_allocation_must_form_cluster()
        {
            AwaitClusterUp(Config.First, Config.Second, Config.Third);
            EnterBarrier("cluster-started");
        }


        private void External_shard_allocation_must_start_cluster_sharding()
        {
            _ = _shardRegion.Value;
            EnterBarrier("shard-region-started");
        }

        private void External_shard_allocation_must_default_to_allocating_a_shard_to_the_local_shard_region()
        {
            RunOn(() =>
            {
                _shardRegion.Value.Tell(new GiveMeYourHome.Get(Myself.Name));
                var actorLocation = ExpectMsg<GiveMeYourHome.Home>(TimeSpan.FromSeconds(20)).Address;
                actorLocation.Should().Be(Cluster.Get(Sys).SelfAddress);
            }, Config.First, Config.Second, Config.Third);
            EnterBarrier("local-message-sent");
        }

        private void External_shard_allocation_must_move_shard_via_distributed_data()
        {
            var shardToSpecifyLocation = "cats";
            RunOn(() =>
            {
                _ = ExternalShardAllocation.Get(Sys)
                    .ClientFor(TypeName)
                    .UpdateShardLocation(shardToSpecifyLocation, Cluster.Get(Sys).SelfAddress)
                    .Result;
            }, Config.First);
            EnterBarrier("shard-location-updated");

            RunOn(() =>
            {
                var probe = CreateTestProbe();
                AwaitAssert(() =>
                {
                    _shardRegion.Value.Tell(new GiveMeYourHome.Get(shardToSpecifyLocation), probe.Ref);
                    probe.ExpectMsg<GiveMeYourHome.Home>(m => m.Address.Equals(GetAddress(Config.First)));
                }, TimeSpan.FromSeconds(10));
            }, Config.Second, Config.Third);
            EnterBarrier("shard-allocated-to-specific-node");
        }

        private void External_shard_allocation_must_allocate_to_a_node_that_does_not_exist_yet()
        {
            var onForthShardId = "on-forth";
            var forthAddress = GetAddress(Config.Forth);
            RunOn(() =>
            {
                Sys.Log.Info("Allocating {0} on {1}", onForthShardId, forthAddress);
                ExternalShardAllocation.Get(Sys).ClientFor(TypeName).UpdateShardLocations(ImmutableDictionary<string, Address>.Empty.Add(onForthShardId, forthAddress));
            }, Config.Second);
            EnterBarrier("allocated-to-new-node");
            RunOn(() =>
            {
                JoinWithin(Config.First, max: TimeSpan.FromSeconds(10));
            }, Config.Forth);
            EnterBarrier("forth-node-joined");
            RunOn(() =>
            {
                AwaitAssert(() =>
                {
                    _shardRegion.Value.Tell(new GiveMeYourHome.Get(InitiallyOnForth));
                    ExpectMsg<GiveMeYourHome.Home>(m => m.Address.Equals(GetAddress(Config.Forth)));
                }, TimeSpan.FromSeconds(10));
            }, Config.First, Config.Second, Config.Third);
            EnterBarrier("shard-allocated-to-forth");
        }

        private void External_shard_allocation_must_move_allocation()
        {
            RunOn(() =>
            {
                Sys.Log.Info("Moving shard from forth to first: {0}", GetAddress(Config.First));
                ExternalShardAllocation.Get(Sys).ClientFor(TypeName).UpdateShardLocation(InitiallyOnForth, GetAddress(Config.First));
            }, Config.Third);
            EnterBarrier("shard-moved-from-forth-to-first");
            RunOn(() =>
            {
                AwaitAssert(() =>
                {
                    _shardRegion.Value.Tell(new GiveMeYourHome.Get(InitiallyOnForth));
                    ExpectMsg<GiveMeYourHome.Home>(m => m.Address.Equals(GetAddress(Config.First)));
                }, TimeSpan.FromSeconds(10));
            }, Config.First, Config.Second, Config.Third, Config.Forth);
            EnterBarrier("finished");
        }
    }
}
