﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis.CodeStyle;
using Microsoft.CodeAnalysis.CSharp.CodeStyle;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace Microsoft.CodeAnalysis.CSharp.UseIndexOperator
{
    using static Helpers;

    /// <summary>
    /// Analyzer that looks for several variants of code like `s.Slice(start, end - start)` and
    /// offers to update to `s[start..end]`.  In order to convert, the type being called on needs a
    /// slice-like method that takes two ints, and returns an instance of the same type. It also
    /// needs a Length/Count property, as well as an indexer that takes a System.Range instance.
    ///
    /// It is assumed that if the type follows this shape that it is well behaved and that this
    /// transformation will preserve semantics.  If this assumption is not good in practice, we
    /// could always limit the feature to only work on a whitelist of known safe types.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp), Shared]
    internal partial class CSharpUseRangeOperatorDiagnosticAnalyzer : AbstractCodeStyleDiagnosticAnalyzer
    {
        public const string UseIndexer = nameof(UseIndexer);
        public const string ComputedRange = nameof(ComputedRange);
        public const string ConstantRange = nameof(ConstantRange);

        public CSharpUseRangeOperatorDiagnosticAnalyzer() 
            : base(IDEDiagnosticIds.UseRangeOperatorDiagnosticId,
                   new LocalizableResourceString(nameof(FeaturesResources.Use_range_operator), FeaturesResources.ResourceManager, typeof(FeaturesResources)),
                   new LocalizableResourceString(nameof(FeaturesResources._0_can_be_simplified), FeaturesResources.ResourceManager, typeof(FeaturesResources)))
        {
        }

        protected override void InitializeWorker(AnalysisContext context)
        {
            context.RegisterCompilationStartAction(compilationContext =>
            {
                // We're going to be checking every invocation in the compilation. Cache information
                // we compute in this object so we don't have to continually recompute it.
                var infoCache = new InfoCache(compilationContext.Compilation);
                compilationContext.RegisterOperationAction(
                    c => AnalyzeInvocation(c, infoCache),
                    OperationKind.Invocation);
            });
        }

        private void AnalyzeInvocation(
            OperationAnalysisContext context, InfoCache infoCache)
        {
            var cancellationToken = context.CancellationToken;
            var invocation = (IInvocationOperation)context.Operation;

            var invocationSyntax = invocation.Syntax as InvocationExpressionSyntax;
            if (invocationSyntax is null ||
                invocationSyntax.ArgumentList is null)
            {
                return;
            }

            // Check if we're at least on C# 8, and that the user wants these operators.
            var syntaxTree = invocationSyntax.SyntaxTree;
            var parseOptions = (CSharpParseOptions)syntaxTree.Options;
            if (parseOptions.LanguageVersion < LanguageVersion.CSharp8)
            {
                return;
            }

            var optionSet = context.Options.GetDocumentOptionSetAsync(syntaxTree, cancellationToken).GetAwaiter().GetResult();
            if (optionSet is null)
            {
                return;
            }

            var option = optionSet.GetOption(CSharpCodeStyleOptions.PreferRangeOperator);
            if (!option.Value)
            {
                return;
            }

            // See if the call is to something slice-like.
            var targetMethod = invocation.TargetMethod;
            if (!IsSliceLikeMethod(invocation.TargetMethod))
            {
                return;
            }

            var sliceLikeMethod = targetMethod;
            // See if this is a type we can use range-indexer for, and also if this is a call to the
            // Slice-Like method we've found for that type.  Use the InfoCache so that we can reuse
            // any previously computed values for this type.
            if (!infoCache.TryGetMemberInfo(sliceLikeMethod, out var memberInfo))
            {
                return;
            }

            // look for `s.Slice(start, end - start)` and convert to `s[Range]`

            // Needs to have the two args for `start` and `end - start`
            if (invocation.Instance is null ||
                invocation.Instance.Syntax is null ||
                invocation.Arguments.Length != 2)
            {
                return;
            }

            // Arg2 needs to be a subtraction for: `end - start`
            var arg2 = invocation.Arguments[1];
            if (!IsSubtraction(arg2, out var subtraction))
            {
                return;
            }

            // See if we have: (start, end - start).  The start operation has to be the same as the
            // right side of the subtraction.
            var startOperation = invocation.Arguments[0].Value;

            if (CSharpSyntaxFactsService.Instance.AreEquivalent(startOperation.Syntax, subtraction.RightOperand.Syntax))
            {
                context.ReportDiagnostic(CreateDiagnostic(
                    ComputedRange, option, invocationSyntax, sliceLikeMethod,
                    memberInfo, startOperation, subtraction.LeftOperand));
                return;
            }

            // See if we have: (constant1, s.Length - constant2).  The constants don't have to be
            // the same value.  This will convert over to s[constant1..(constant - constant1)]
            if (IsConstantInt32(startOperation) &&
                IsConstantInt32(subtraction.RightOperand) &&
                IsInstanceLengthCheck(memberInfo.LengthLikeProperty, invocation.Instance, subtraction.LeftOperand))
            {
                context.ReportDiagnostic(CreateDiagnostic(
                    ConstantRange, option, invocationSyntax, sliceLikeMethod,
                    memberInfo, startOperation, subtraction.RightOperand));
                return;
            }
        }

        private Diagnostic CreateDiagnostic(
            string rangeKind, CodeStyleOption<bool> option, InvocationExpressionSyntax invocation, 
            IMethodSymbol sliceLikeMethod, MemberInfo memberInfo, IOperation op1, IOperation op2)
        {
            var properties = ImmutableDictionary<string, string>.Empty.Add(rangeKind, rangeKind);

            if (memberInfo.SliceRangeMethodOpt == null)
            {
                properties = properties.Add(UseIndexer, UseIndexer);
            }

            // Keep track of the syntax nodes from the start/end ops so that we can easily 
            // generate the range-expression in the fixer.
            var additionalLocations = ImmutableArray.Create(
                invocation.GetLocation(),
                op1.Syntax.GetLocation(),
                op2.Syntax.GetLocation());

            // Mark the span under the two arguments to .Slice(..., ...) as what we will be
            // updating.
            var arguments = invocation.ArgumentList.Arguments;
            var location = Location.Create(invocation.SyntaxTree,
                TextSpan.FromBounds(arguments.First().SpanStart, arguments.Last().Span.End));

            return DiagnosticHelper.Create(
                Descriptor,
                location,
                option.Notification.Severity,
                additionalLocations,
                properties,
                sliceLikeMethod.Name);
        }

        private static bool IsConstantInt32(IOperation operation)
            => operation.ConstantValue.HasValue && operation.ConstantValue.Value is int;
    }
}
