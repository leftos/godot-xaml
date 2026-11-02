using GodotXaml.Core.Tscn;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Core.Conversion;

public static partial class XscnToTscn
{
    private sealed partial class Context
    {
        private const string RootLocation = "root";

        // An .xres becomes a .tres: the root resource's properties are the [resource] section's, and the header takes
        // its type, the script's global class and the uid the host passes (resource_format_text.cpp:1801-1819).
        public TscnDocument ConvertResource()
        {
            if (document.Root is not XscnResource root)
            {
                Report(ConversionCodes.NotAResource, ConversionSeverity.Error, "The root of a resource document is a node.", RootLocation);
                return new TscnDocument
                {
                    Kind = TscnKind.Resource,
                    Header = new TscnHeader { Uid = options.Uid },
                };
            }

            Discover(root, RootLocation);
            ReportUnusedEntries();
            TscnDocument converted = MintIds(new TscnDocument { Kind = TscnKind.Resource }) with
            {
                ResourceProperties = Properties(root, RootLocation),
            };
            var header = new TscnHeader
            {
                Type = TypeName(root.Type, RootLocation),
                ScriptClass = ScriptClass(root),
                Uid = options.Uid,
                Format = TscnWriter.FormatOf(converted),
            };
            return converted with { Header = header };
        }

        // Godot saves only the resources it finds from the main one, so an entry nothing refers to is not written.
        private void ReportUnusedEntries()
        {
            foreach (XscnResource entry in table.Values.Where(entry => !found.Contains(entry)))
            {
                string message = "Nothing refers to this x:Resources entry; Godot would not save it, so it is dropped.";
                Report(ConversionCodes.UnusedResourceEntry, ConversionSeverity.Warning, message, $"x:Resources \"{entry.Key}\"");
            }
        }

        // resource_format_text.cpp:1803-1807: script_class is the script's global name, which only the host can read.
        private string? ScriptClass(XscnResource root)
        {
            if (root.Properties.LastOrDefault(property => property.GodotName == "script")?.Value is not XscnFile script)
            {
                return null;
            }

            if (options.ScriptClassOf is null)
            {
                string message = $"No ScriptClassOf lookup says the global class of '{script.Path}'; script_class is not written.";
                Report(ConversionCodes.ScriptClassUnknown, ConversionSeverity.Warning, message, RootLocation);
                return null;
            }

            return options.ScriptClassOf(script.Path);
        }
    }
}
