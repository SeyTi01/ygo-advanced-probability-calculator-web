using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace YGOProbabilityCalculatorBlazor.Services.Session;

/// <summary>Offline transport of the existing session JSON; independent of its schema version.</summary>
public static class SessionShareCodec
{
    public const string Namespace = "#ygo-session=";
    public const string Prefix = Namespace + "v1.";
    public const int MaxUrlLength = 16_384;
    public const int MaxJsonBytes = 262_144;
    private const int HeaderLength = 36;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private static readonly HashSet<string> Collections =
        new([
                "Cards", "Categories", "Combos", "ComboGroups", "ManualMetadataCategoryKeys", "AlternativeGroups",
                "Alternatives"
            ],
            StringComparer.OrdinalIgnoreCase);

    public static string CreateLink(string baseUri, string serializedSession)
    {
        byte[] source = StrictUtf8.GetBytes(serializedSession);

        if (source.Length > MaxJsonBytes)
        {
            throw TooLarge();
        }

        using JsonDocument document = Parse(source);
        using MemoryStream compact = new();

        using (Utf8JsonWriter writer = new(compact))
        {
            document.RootElement.WriteTo(writer);
        }

        byte[] json = compact.ToArray();
        using MemoryStream compressed = new();

        using (GZipStream encoder = new(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            encoder.Write(json);
        }

        byte[] compressedBytes = compressed.ToArray();
        byte[] transport = new byte[HeaderLength + compressedBytes.Length];
        BinaryPrimitives.WriteInt32LittleEndian(transport, json.Length);
        SHA256.HashData(compressedBytes, transport.AsSpan(4, 32));
        compressedBytes.CopyTo(transport, HeaderLength);
        string payload = Convert.ToBase64String(transport).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        string origin = new UriBuilder(baseUri) { Query = "", Fragment = "" }.Uri.AbsoluteUri;
        string link = origin + Prefix + payload;

        if (link.Length > MaxUrlLength)
        {
            throw TooLarge();
        }

        return link;
    }

    public static string Decode(string fragment)
    {
        if (fragment.Length > MaxUrlLength)
        {
            throw TooLarge();
        }

        if (! fragment.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Unsupported shared-link format version.");
        }

        string payload = fragment[Prefix.Length..];

        if (payload.Length == 0 || payload.Length % 4 == 1 || payload.Any(c =>
                ! char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_'))
        {
            throw Invalid();
        }

        try
        {
            byte[] transport = Convert.FromBase64String(payload.Replace('-', '+').Replace('_', '/') +
                                                        new string('=', (4 - payload.Length % 4) % 4));

            if (transport.Length <= HeaderLength)
            {
                throw Invalid();
            }

            // Length is checked before allocation. Decoder receives only this bounded destination.
            int length = BinaryPrimitives.ReadInt32LittleEndian(transport);

            if (length < 2 || length > MaxJsonBytes)
            {
                throw TooLarge();
            }

            byte[] json = new byte[length];
            Span<byte> compressedBytes = transport.AsSpan(HeaderLength);

            if (compressedBytes.Length < 18 || compressedBytes[0] != 0x1f || compressedBytes[1] != 0x8b ||
                BinaryPrimitives.ReadUInt32LittleEndian(compressedBytes[^4..]) != length ||
                ! CryptographicOperations.FixedTimeEquals(SHA256.HashData(compressedBytes), transport.AsSpan(4, 32)))
            {
                throw Invalid();
            }

            using MemoryStream input = new(transport, HeaderLength, transport.Length - HeaderLength);
            using GZipStream decoder = new(input, CompressionMode.Decompress);
            decoder.ReadExactly(json);

            if (decoder.ReadByte() != -1)
            {
                throw Invalid();
            }

            string text = StrictUtf8.GetString(json);
            using JsonDocument document = Parse(json);

            return text;
        }
        catch (Exception ex) when (ex is FormatException or DecoderFallbackException or JsonException
                                       or InvalidDataException or EndOfStreamException)
        {
            throw Invalid();
        }
    }

    private static JsonDocument Parse(byte[] json)
    {
        JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });

        try
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw Invalid();
            }

            int nodes = 0;
            Check(document.RootElement, ref nodes);

            return document;
        }
        catch
        {
            document.Dispose();

            throw;
        }
    }

    private static void Check(JsonElement element, ref int nodes)
    {
        if (++nodes > 20_000)
        {
            throw TooLarge();
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);

                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (property.Name.Length > 4096 || ! names.Add(property.Name))
                    {
                        throw Invalid();
                    }

                    // The file loader intentionally tolerates some absent fields. Explicit null
                    // model nodes must never reach application after result invalidation starts.
                    if (Collections.Contains(property.Name) && property.Value.ValueKind != JsonValueKind.Array)
                    {
                        throw Invalid();
                    }

                    if ((property.Name.Equals("BaseCategory", StringComparison.OrdinalIgnoreCase) ||
                         property.Name.Equals("CategoryColorIndices", StringComparison.OrdinalIgnoreCase)) &&
                        property.Value.ValueKind != JsonValueKind.Object)
                    {
                        throw Invalid();
                    }

                    if (property.Name.Equals("Copies", StringComparison.OrdinalIgnoreCase) &&
                        (property.Value.ValueKind != JsonValueKind.Number ||
                         ! property.Value.TryGetInt32(out int copies) || copies is < -10000 or > 10000))
                    {
                        throw Invalid();
                    }

                    Check(property.Value, ref nodes);
                }

                break;
            case JsonValueKind.Array:
                if (element.GetArrayLength() > 2048)
                {
                    throw TooLarge();
                }

                foreach (JsonElement item in element.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Null)
                    {
                        throw Invalid();
                    }

                    Check(item, ref nodes);
                }

                break;
            case JsonValueKind.String:
                if (element.GetString()!.Length > 4096)
                {
                    throw TooLarge();
                }

                break;
        }
    }

    private static InvalidOperationException TooLarge() =>
        new("This session is too large for a share link. Share a normal session file instead.");

    private static InvalidOperationException Invalid() => new("The shared session link is invalid or corrupted.");
}
