using System.Buffers.Binary;
using System.Text;

namespace Guardian.Core.Network;

/// <summary>Minimal DNS wire-format helpers: read the first question, build a refusal answer.</summary>
public static class DnsMessage
{
    public sealed record Question(string Name, ushort Type, int EndOffset);

    public static Question? ReadQuestion(ReadOnlySpan<byte> msg)
    {
        if (msg.Length < 12 || BinaryPrimitives.ReadUInt16BigEndian(msg[4..]) < 1) return null;
        var pos = 12;
        var sb = new StringBuilder();
        while (true)
        {
            if (pos >= msg.Length) return null;
            int len = msg[pos++];
            if (len == 0) break;
            if (len > 63 || pos + len > msg.Length) return null; // no compression in questions
            if (sb.Length > 0) sb.Append('.');
            sb.Append(Encoding.ASCII.GetString(msg.Slice(pos, len)));
            pos += len;
            if (sb.Length > 253) return null;
        }
        if (pos + 4 > msg.Length) return null;
        var type = BinaryPrimitives.ReadUInt16BigEndian(msg[pos..]);
        return new Question(sb.ToString().ToLowerInvariant(), type, pos + 4);
    }

    public const ushort TypeA = 1, TypeAAAA = 28;

    /// <summary>
    /// Answer for a blocked name: A -> 0.0.0.0, AAAA -> ::, anything else -> NXDOMAIN.
    /// Returning an unroutable address makes browsers fail instantly instead of retrying.
    /// </summary>
    public static byte[] BuildBlocked(ReadOnlySpan<byte> query)
    {
        var q = ReadQuestion(query) ?? throw new ArgumentException("Malformed query.");
        var header = query[..q.EndOffset].ToArray();
        // QR=1, keep opcode + RD, set RA.
        header[2] = (byte)(0x80 | (header[2] & 0x79));
        header[3] = 0x80;
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(6), 0); // ANCOUNT
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(8), 0); // NSCOUNT
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(10), 0); // ARCOUNT
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(4), 1); // QDCOUNT

        byte[]? rdata = q.Type switch { TypeA => new byte[4], TypeAAAA => new byte[16], _ => null };
        if (rdata is null)
        {
            header[3] = 0x83; // RA + NXDOMAIN
            return header;
        }
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(6), 1);
        var answer = new byte[12 + rdata.Length];
        answer[0] = 0xC0; answer[1] = 0x0C;                              // pointer to question name
        BinaryPrimitives.WriteUInt16BigEndian(answer.AsSpan(2), q.Type);
        BinaryPrimitives.WriteUInt16BigEndian(answer.AsSpan(4), 1);      // IN
        BinaryPrimitives.WriteUInt32BigEndian(answer.AsSpan(6), 60);     // TTL
        BinaryPrimitives.WriteUInt16BigEndian(answer.AsSpan(10), (ushort)rdata.Length);
        rdata.CopyTo(answer, 12);
        return [.. header, .. answer];
    }

    /// <summary>SERVFAIL for the query, so the client retries quickly instead of timing out.</summary>
    public static byte[] BuildServFail(ReadOnlySpan<byte> query)
    {
        var q = ReadQuestion(query) ?? throw new ArgumentException("Malformed query.");
        var header = query[..q.EndOffset].ToArray();
        header[2] = (byte)(0x80 | (header[2] & 0x79));
        header[3] = 0x82; // RA + SERVFAIL
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(6), 0);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(8), 0);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(10), 0);
        return header;
    }

    /// <summary>Builds a standard query (used in tests and health checks).</summary>
    public static byte[] BuildQuery(ushort id, string name, ushort type)
    {
        var bytes = new List<byte>();
        Span<byte> h = stackalloc byte[12];
        BinaryPrimitives.WriteUInt16BigEndian(h, id);
        h[2] = 0x01; // RD
        BinaryPrimitives.WriteUInt16BigEndian(h[4..], 1);
        bytes.AddRange(h.ToArray());
        foreach (var label in name.Split('.'))
        {
            bytes.Add((byte)label.Length);
            bytes.AddRange(Encoding.ASCII.GetBytes(label));
        }
        bytes.Add(0);
        bytes.Add((byte)(type >> 8)); bytes.Add((byte)type);
        bytes.Add(0); bytes.Add(1);
        return bytes.ToArray();
    }
}
