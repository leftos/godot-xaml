using System;
using System.Xml;
using System.Xml.Linq;
using Godot;

namespace XscnSpike;

/// <summary>Loads a spike <c>.xscn</c> file (one XML element per node) into a <see cref="PackedScene"/>.</summary>
[GlobalClass]
[Tool]
public partial class XscnFormatLoader : ResourceFormatLoader
{
    private const string InstanceElement = "Instance";

    public override string[] _GetRecognizedExtensions() => ["xscn"];

    public override bool _HandlesType(StringName type) => type == "PackedScene" || type == "Resource";

    public override string _GetResourceType(string path) => path.GetExtension().ToLowerInvariant() == "xscn" ? "PackedScene" : "";

    public override Variant _Load(string path, string originalPath, bool useSubThreads, int cacheMode)
    {
        Node? root = null;
        try
        {
            XDocument document = XDocument.Parse(FileAccess.GetFileAsString(path));
            XElement rootElement = document.Root ?? throw new InvalidOperationException("the document has no root element");
            root = BuildNode(rootElement);
            AddChildren(rootElement, root, root);

            var packed = new PackedScene();
            Error packError = packed.Pack(root);
            if (packError != Error.Ok)
            {
                throw new InvalidOperationException($"PackedScene.Pack returned {packError}");
            }

            return packed;
        }
        catch (Exception exception) when (exception is XmlException or InvalidOperationException)
        {
            GD.PushError($"xscn: cannot load {path}: {exception.Message}");
            return (int)Error.FileCorrupt;
        }
        finally
        {
            root?.Free();
        }
    }

    private static void AddChildren(XElement element, Node parent, Node sceneRoot)
    {
        foreach (XElement childElement in element.Elements())
        {
            Node child = BuildNode(childElement);
            parent.AddChild(child);
            child.Owner = sceneRoot;
            if (childElement.Name.LocalName != InstanceElement)
            {
                AddChildren(childElement, child, sceneRoot);
            }
        }
    }

    private static Node BuildNode(XElement element)
    {
        string elementName = element.Name.LocalName;
        if (elementName == InstanceElement)
        {
            return BuildInstance(element);
        }

        if (ClassDB.Instantiate(elementName).AsGodotObject() is not Node node)
        {
            throw new InvalidOperationException($"'{elementName}' is not an instantiable Node class");
        }

        try
        {
            foreach (XAttribute attribute in element.Attributes())
            {
                ApplyAttribute(node, attribute);
            }
        }
        catch (InvalidOperationException)
        {
            node.Free();
            throw;
        }

        return node;
    }

    private static Node BuildInstance(XElement element)
    {
        string scenePath = element.Attribute("Scene")?.Value
            ?? throw new InvalidOperationException("an Instance element has no Scene attribute");
        PackedScene scene = ResourceLoader.Load<PackedScene>(scenePath)
            ?? throw new InvalidOperationException($"cannot load the instanced scene {scenePath}");
        Node node = scene.Instantiate(PackedScene.GenEditState.Instance);
        string? name = element.Attribute("Name")?.Value;
        if (name is not null)
        {
            node.Name = name;
        }

        return node;
    }

    private static void ApplyAttribute(Node node, XAttribute attribute)
    {
        string attributeName = attribute.Name.LocalName;
        switch (attributeName)
        {
            case "Name":
                node.Name = attribute.Value;
                break;
            case "Script":
                node.SetScript(GD.Load<Script>(attribute.Value)
                    ?? throw new InvalidOperationException($"cannot load the script {attribute.Value}"));
                break;
            default:
                node.Set(attributeName, GD.StrToVar(attribute.Value));
                break;
        }
    }
}
