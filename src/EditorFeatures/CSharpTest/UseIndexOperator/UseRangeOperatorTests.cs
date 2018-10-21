﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.UseIndexOperator;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Editor.CSharp.UnitTests.Diagnostics;
using Microsoft.CodeAnalysis.Test.Utilities;
using Xunit;

namespace Microsoft.CodeAnalysis.Editor.CSharp.UnitTests.UseIndexOperator
{
    public class UseRangeOperatorTests : AbstractCSharpDiagnosticProviderBasedUserDiagnosticTest
    {
        internal override (DiagnosticAnalyzer, CodeFixProvider) CreateDiagnosticProviderAndFixer(Workspace workspace)
            => (new CSharpUseRangeOperatorDiagnosticAnalyzer(), new CSharpUseRangeOperatorCodeFixProvider());

        private static readonly CSharpParseOptions s_parseOptions = 
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp8);

        private static readonly TestParameters s_testParameters =
            new TestParameters(parseOptions: s_parseOptions);

        [Fact, Trait(Traits.Feature, Traits.Features.CodeActionsUseRangeOperator)]
        public async Task TestNotInCSharp7()
        {
            await TestMissingAsync(
@"
class C
{
    void Goo(string s)
    {
        var v = s.Substring([||]1, s.Length - 1);
    }
}", parameters: new TestParameters(
    parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp7)));
        }

        [Fact, Trait(Traits.Feature, Traits.Features.CodeActionsUseRangeOperator)]
        public async Task TestSimple()
        {
            await TestAsync(
@"
class C
{
    void Goo(string s)
    {
        var v = s.Substring([||]1, s.Length - 1);
    }
}",
@"
class C
{
    void Goo(string s)
    {
        var v = s[1..];
    }
}", parseOptions: s_parseOptions);
        }

        [Fact, Trait(Traits.Feature, Traits.Features.CodeActionsUseRangeOperator)]
        public async Task TestComplexSubstraction()
        {
            await TestAsync(
@"
class C
{
    void Goo(string s, int bar, int baz)
    {
        var v = s.Substring([||]bar, s.Length - baz - bar);
    }
}",
@"
class C
{
    void Goo(string s, int bar, int baz)
    {
        var v = s[bar..^baz];
    }
}", parseOptions: s_parseOptions);
        }

        [Fact, Trait(Traits.Feature, Traits.Features.CodeActionsUseRangeOperator)]
        public async Task TestConstantSubtraction1()
        {
            await TestAsync(
@"
class C
{
    void Goo(string s)
    {
        var v = s.Substring([||]1, s.Length - 2);
    }
}",
@"
class C
{
    void Goo(string s)
    {
        var v = s[1..^1];
    }
}", parseOptions: s_parseOptions);
        }

        [Fact, Trait(Traits.Feature, Traits.Features.CodeActionsUseRangeOperator)]
        public async Task TestNonStringType()
        {
            await TestAsync(
@"
namespace System { public struct Range { } }
struct S { public S Slice(int start, int length); public int Length { get; } public S this[System.Range] { get; } }
class C
{
    void Goo(S s)
    {
        var v = s.Slice([||]1, s.Length - 2);
    }
}",
@"
namespace System { public struct Range { } }
struct S { public S Slice(int start, int length); public int Length { get; } public S this[System.Range] { get; } }
class C
{
    void Goo(S s)
    {
        var v = s[1..^1];
    }
}", parseOptions: s_parseOptions);
        }

        [Fact, Trait(Traits.Feature, Traits.Features.CodeActionsUseRangeOperator)]
        public async Task TestMethodToMethod()
        {
            await TestAsync(
@"
namespace System { public struct Range { } }
struct S { public int Slice(int start, int length); public int Length { get; } public int Slice(System.Range r); }
class C
{
    void Goo(S s)
    {
        var v = s.Slice([||]1, s.Length - 2);
    }
}",
@"
namespace System { public struct Range { } }
struct S { public int Slice(int start, int length); public int Length { get; } public int Slice(System.Range r); }
class C
{
    void Goo(S s)
    {
        var v = s.Slice(1..^1);
    }
}", parseOptions: s_parseOptions);
        }
    }
}
