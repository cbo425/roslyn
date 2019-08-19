﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace Microsoft.CodeAnalysis.FindUsages
{
    using ReferenceUsageInfoMap = ImmutableDictionary<string, ImmutableArray<string>>;

    /// <summary>
    /// Information about a symbol's reference that can be used for display and 
    /// navigation in an editor.
    /// </summary>
    internal sealed class SourceReferenceItem
    {
        // We can have only a handful of different values for enums within SymbolUsageInfo, so the maximum size of this dictionary is capped.
        // So, we store this as a static dictionary which will be held in memory for the lifetime of the process.
        private static readonly ConcurrentDictionary<SymbolUsageInfo, ReferenceUsageInfoMap> s_symbolUsageInfoToReferenceInfoMap
            = new ConcurrentDictionary<SymbolUsageInfo, ReferenceUsageInfoMap>();

        /// <summary>
        /// The definition this reference corresponds to.
        /// </summary>
        public DefinitionItem Definition { get; }

        /// <summary>
        /// The location of the source item.
        /// </summary>
        public DocumentSpan SourceSpan { get; }

        /// <summary>
        /// If this reference is a location where the definition is written to.
        /// </summary>
        public bool IsWrittenTo { get; }

        /// <summary>
        /// Additional information about the reference.
        /// Each entry represents a key-values pair of data. For example, consider the below entry:
        ///     { "ValueUsageInfo" } = { "Read", "Write" }
        /// This entry indicates that the reference has additional value usage information which indicate
        /// it is a read/write reference, such as say 'a++'.
        /// </summary>
        public ReferenceUsageInfoMap ReferenceUsageInfo { get; }

        /// <summary>
        /// Additional properties for the reference, similar to ReferenceUsageInfo.
        /// These property values are a single string, so do not need to allocate an array of strings.
        /// </summary>
        public ImmutableArray<AdditionalProperty> AdditionalProperties { get; }

        [Obsolete]
        public SourceReferenceItem(DefinitionItem definition, DocumentSpan sourceSpan, bool isWrittenTo)
        {
            Definition = definition;
            SourceSpan = sourceSpan;
            IsWrittenTo = isWrittenTo;
            ReferenceUsageInfo = ReferenceUsageInfoMap.Empty;
        }

        public SourceReferenceItem(DefinitionItem definition, DocumentSpan sourceSpan, ReferenceUsageInfoMap referenceInfo)
        {
            Definition = definition;
            SourceSpan = sourceSpan;
            ReferenceUsageInfo = referenceInfo ?? ReferenceUsageInfoMap.Empty;
        }

        // Being used by TypeScript
        internal SourceReferenceItem(DefinitionItem definition, DocumentSpan sourceSpan, SymbolUsageInfo symbolUsageInfo)
            : this(definition, sourceSpan, GetOrCreateReferenceUsageInfo(symbolUsageInfo))
        {
            IsWrittenTo = symbolUsageInfo.IsWrittenTo();
        }

        internal SourceReferenceItem(DefinitionItem definition, DocumentSpan sourceSpan, SymbolUsageInfo symbolUsageInfo, ImmutableArray<AdditionalProperty> additionalProperties)
            : this(definition, sourceSpan, GetOrCreateReferenceUsageInfo(symbolUsageInfo))
        {
            IsWrittenTo = symbolUsageInfo.IsWrittenTo();
            AdditionalProperties = additionalProperties;
        }

        private static ReferenceUsageInfoMap GetOrCreateReferenceUsageInfo(SymbolUsageInfo symbolUsageInfo) 
            => s_symbolUsageInfoToReferenceInfoMap.GetOrAdd(symbolUsageInfo, v => CreateReferenceUsageInfo(v));

        private static ReferenceUsageInfoMap CreateReferenceUsageInfo(SymbolUsageInfo symbolUsageInfo)
        {
            var referenceUsageInfoMap = ReferenceUsageInfoMap.Empty;
            if (!symbolUsageInfo.Equals(SymbolUsageInfo.None))
            {
                referenceUsageInfoMap = referenceUsageInfoMap.Add(nameof(SymbolUsageInfo), symbolUsageInfo.ToLocalizableValues());
            }

            return referenceUsageInfoMap;
        }
    }
}
