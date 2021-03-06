using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Models.AutoComplete;

namespace OmniSharp.LanguageServerProtocol.Handlers
{
    class OmniSharpCompletionHandler : CompletionHandler
    {
        public static IEnumerable<IJsonRpcHandler> Enumerate(RequestHandlers handlers)
        {

            foreach (var (selector, handler) in handlers
                .OfType<Mef.IRequestHandler<AutoCompleteRequest, IEnumerable<AutoCompleteResponse>>>())
                if (handler != null)
                    yield return new OmniSharpCompletionHandler(handler, selector);
        }

        private readonly Mef.IRequestHandler<AutoCompleteRequest, IEnumerable<AutoCompleteResponse>> _autoCompleteHandler;

        private static readonly IDictionary<string, CompletionItemKind> _kind = new Dictionary<string, CompletionItemKind>{
            // types
            { "Class",  CompletionItemKind.Class },
            { "Delegate", CompletionItemKind.Function },
            { "Enum", CompletionItemKind.Enum },
            { "Interface", CompletionItemKind.Interface },
            { "Struct", CompletionItemKind.Struct },

            // variables
            { "Local", CompletionItemKind.Variable },
            { "Parameter", CompletionItemKind.Variable },
            { "RangeVariable", CompletionItemKind.Variable },

            // members
            { "Const", CompletionItemKind.Constant },
            { "EnumMember", CompletionItemKind.Enum },
            { "Event", CompletionItemKind.Event }, 
            { "Field", CompletionItemKind.Field },
            { "Method", CompletionItemKind.Method },
            { "Property", CompletionItemKind.Property },

            // other stuff
            { "Label", CompletionItemKind.Text },
            { "Keyword", CompletionItemKind.Keyword },
            { "Namespace", CompletionItemKind.Module }
        };

        private static CompletionItemKind GetCompletionItemKind(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return CompletionItemKind.Property;
            }
            if (_kind.TryGetValue(key, out var completionItemKind))
            {
                return completionItemKind;
            }
            return CompletionItemKind.Property;
        }

        public OmniSharpCompletionHandler(Mef.IRequestHandler<AutoCompleteRequest, IEnumerable<AutoCompleteResponse>> autoCompleteHandler, DocumentSelector documentSelector)
            : base(new CompletionRegistrationOptions()
            {
                DocumentSelector = documentSelector,
                // TODO: Come along and add a service for getting autocompletion details after the fact.
                ResolveProvider = false,
                TriggerCharacters = new[] { ".", },
            })
        {
            _autoCompleteHandler = autoCompleteHandler;
        }

        public async override Task<CompletionList> Handle(CompletionParams request, CancellationToken token)
        {
            var omnisharpRequest = new AutoCompleteRequest()
            {
                FileName = Helpers.FromUri(request.TextDocument.Uri),
                Column = Convert.ToInt32(request.Position.Character),
                Line = Convert.ToInt32(request.Position.Line),
                WantKind = true,
                WantDocumentationForEveryCompletionResult = true,
                WantReturnType = true,
                WantSnippet = Capability.CompletionItem?.SnippetSupport ?? false
            };

            var omnisharpResponse = await _autoCompleteHandler.Handle(omnisharpRequest);

            var completions = new Dictionary<string, List<CompletionItem>>();
            foreach (var response in omnisharpResponse)
            {
                var isSnippet = !string.IsNullOrEmpty(response.Snippet);
                var text = isSnippet ? response.Snippet : response.CompletionText;
                var textFormat = isSnippet ? InsertTextFormat.Snippet : InsertTextFormat.PlainText;

                var completionItem = new CompletionItem
                {
                    Label = response.CompletionText,
                    Detail = string.IsNullOrEmpty(response.ReturnType) ?
                            response.DisplayText :
                            $"{response.ReturnType} {response.DisplayText}",
                    Documentation = response.Description,
                    Kind = GetCompletionItemKind(response.Kind),
                    InsertText = text,
                    InsertTextFormat = textFormat,
                };

                if (!completions.ContainsKey(completionItem.Label))
                {
                    completions[completionItem.Label] = new List<CompletionItem>();
                }
                completions[completionItem.Label].Add(completionItem);
            }

            var result = new List<CompletionItem>();
            foreach (var key in completions.Keys)
            {
                var suggestion = completions[key][0];
                var overloadCount = completions[key].Count - 1;

                if (overloadCount > 0)
                {
                    // indicate that there is more
                    suggestion.Detail = $"{suggestion.Detail} (+ {overloadCount} overload(s))";
                }

                result.Add(suggestion);
            }

            return new CompletionList(result);
        }

        public override Task<CompletionItem> Handle(CompletionItem request, CancellationToken cancellationToken)
        {
            return Task.FromResult(request);
        }
    }
}
