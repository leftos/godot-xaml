using System;
using System.IO;
using System.Text;
using System.Xml;
using Godot;
using Godot.Collections;
using FileAccess = Godot.FileAccess;

namespace XscnSpike;

/// <summary>Saves a <see cref="PackedScene"/> as a spike <c>.xscn</c> file, writing only stored properties that differ from their defaults.</summary>
[GlobalClass]
[Tool]
public partial class XscnFormatSaver : ResourceFormatSaver
{
    private static readonly XmlWriterSettings WriterSettings = new()
    {
        Encoding = new UTF8Encoding(false),
        Indent = true,
        IndentChars = "  ",
        NewLineChars = "\n",
        NewLineHandling = NewLineHandling.Replace,
        OmitXmlDeclaration = false,
    };

    public override string[] _GetRecognizedExtensions(Resource resource) => resource is PackedScene ? ["xscn"] : [];

    public override bool _Recognize(Resource resource) => resource is PackedScene;

    public override bool _RecognizePath(Resource resource, string path) => path.GetExtension().ToLowerInvariant() == "xscn";

    public override Error _Save(Resource resource, string path, uint flags)
    {
        if (resource is not PackedScene scene)
        {
            GD.PushError($"xscn: cannot save {path}: the resource is a {resource.GetClass()}, not a PackedScene");
            return Error.InvalidParameter;
        }

        Node root = scene.Instantiate(PackedScene.GenEditState.Instance);
        try
        {
            byte[] bytes = Serialize(root, path);
            using FileAccess? file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
            if (file is null)
            {
                Error openError = FileAccess.GetOpenError();
                GD.PushError($"xscn: cannot save {path}: FileAccess.Open returned {openError}");
                return openError;
            }

            file.StoreBuffer(bytes);
            return Error.Ok;
        }
        catch (Exception exception) when (exception is XmlException or ArgumentException or InvalidOperationException)
        {
            GD.PushError($"xscn: cannot save {path}: {exception.Message}");
            return Error.Failed;
        }
        finally
        {
            root.Free();
        }
    }

    private static byte[] Serialize(Node root, string path)
    {
        using var stream = new MemoryStream();
        int skipped = 0;
        using (var writer = XmlWriter.Create(stream, WriterSettings))
        {
            writer.WriteStartDocument();
            WriteNode(writer, root, root, ref skipped);
            writer.WriteEndDocument();
        }

        if (skipped > 0)
        {
            GD.Print($"xscn: {path}: skipped {skipped} object-valued properties");
        }

        stream.WriteByte((byte)'\n');
        return stream.ToArray();
    }

    private static void WriteNode(XmlWriter writer, Node node, Node sceneRoot, ref int skipped)
    {
        if (node != sceneRoot && !string.IsNullOrEmpty(node.SceneFilePath))
        {
            writer.WriteStartElement("Instance");
            writer.WriteAttributeString("Name", node.Name);
            writer.WriteAttributeString("Scene", node.SceneFilePath);
            writer.WriteEndElement();
            return;
        }

        writer.WriteStartElement(node.GetClass());
        writer.WriteAttributeString("Name", node.Name);
        if (node.GetScript().AsGodotObject() is Script script)
        {
            writer.WriteAttributeString("Script", script.ResourcePath);
        }

        WriteProperties(writer, node, ref skipped);

        foreach (Node child in node.GetChildren())
        {
            if (child.Owner == sceneRoot)
            {
                WriteNode(writer, child, sceneRoot, ref skipped);
            }
        }

        writer.WriteEndElement();
    }

    private static void WriteProperties(XmlWriter writer, Node node, ref int skipped)
    {
        string className = node.GetClass();
        foreach (Dictionary property in node.GetPropertyList())
        {
            var usage = (PropertyUsageFlags)property["usage"].AsInt64();
            string name = property["name"].AsString();
            if (!usage.HasFlag(PropertyUsageFlags.Storage) || name == "script")
            {
                continue;
            }

            Variant value = node.Get(name);
            if (value.VariantType == Variant.Type.Object && value.AsGodotObject() is not null)
            {
                skipped++;
                continue;
            }

            string text = GD.VarToStr(value);
            if (text != GD.VarToStr(ClassDB.ClassGetPropertyDefaultValue(className, name)))
            {
                writer.WriteAttributeString(name, text);
            }
        }
    }
}
