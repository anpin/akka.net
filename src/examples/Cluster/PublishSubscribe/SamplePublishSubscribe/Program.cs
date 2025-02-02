﻿//-----------------------------------------------------------------------
// <copyright file="Program.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#region subscriber
// See https://aka.ms/new-console-template for more information
using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Configuration;
using SampleSubscriber;

var config = ConfigurationFactory.ParseString(@"
akka {
   actor.provider = cluster
   extensions = [""Akka.Cluster.Tools.PublishSubscribe.DistributedPubSubExtensionProvider,Akka.Cluster.Tools""]
   remote {
       dot-netty.tcp {
           port = 0
           hostname = localhost
       }
   }
   cluster {
       seed-nodes = [""akka.tcp://cluster@localhost:5800""]
   }
}");
var actorSystem = ActorSystem.Create("cluster", config);

DistributedPubSub.Get(actorSystem);

actorSystem.ActorOf(Props.Create(() => new Subscriber()), "subscriber");

actorSystem.WhenTerminated.Wait();
#endregion
