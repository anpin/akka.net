﻿//-----------------------------------------------------------------------
// <copyright file="Tests.fs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

[<AutoOpen>]
module Tests

open Xunit

let equals (expected: 'a) (value: 'a) = Assert.Equal<'a>(expected, value) 
let notEquals (notExpected: 'a) (value: 'a) = Assert.NotEqual<'a>(notExpected, value) 
let success = ()

