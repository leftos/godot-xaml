using System.Collections.Immutable;
using GodotXaml.Core.Xscn;

namespace GodotXaml.Core.Conversion;

public static partial class TscnToXscn
{
    private sealed partial class Context
    {
        // A .tres becomes an .xres whose root tag is the main resource's class and whose root properties are the
        // [resource] section's.
        public XscnDocument ConvertResource()
        {
            ImmutableArray<XscnResource> table = Table();
            var root = new XscnResource
            {
                Type = TypeOf(document.Header.Type ?? "Resource", "[gd_resource]"),
                Properties = Properties(document.ResourceProperties, "[resource]"),
            };
            return new XscnDocument(XscnDocumentKind.Resource, [], Uids(), table, root);
        }
    }
}
