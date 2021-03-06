namespace TapeArchive;

using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Represents an archive item of POSIX.1-1988 TAR ("ustar" or Unix Standard TAR).
/// </summary>
public class UstarItem : PrePosixItem
{
    public UstarItem(ItemType type, ItemName name)
        : base(type, name)
    {
    }

    protected internal UstarItem()
    {
    }

    protected override byte PreferredNumericLeading => (byte)'0';

    protected internal override UstarItem CreateParent(ItemName name, ParentProperties? props)
    {
        if (!name.IsDirectory)
        {
            throw new ArgumentException("The value is not a directory.", nameof(name));
        }

        return new(PrePosixType.Directory, name)
        {
            Mode = props?.Mode ?? GetParentMode(this.Mode),
            UserId = props?.UserId ?? this.UserId,
            GroupId = props?.GroupId ?? this.GroupId,
            ModificationTime = props?.ModificationTime ?? this.ModificationTime,
            UserName = (props as PrePosixParentProperties)?.UserName ?? this.UserName,
            GroupName = (props as PrePosixParentProperties)?.GroupName ?? this.GroupName,
        };
    }

    protected override bool IsNumericLeading(byte value)
    {
        return value == '0';
    }

    protected override ValueTask<ItemName> ParseNameAsync(HeaderCollection headers, CancellationToken cancellationToken = default)
    {
        // Parse value.
        var header = headers[0];
        var name = header[..100].Span;
        var prefix = header[345..500].Span;
        string value;

        if (prefix[0] != 0)
        {
            value = $"{ParseAscii(prefix)}/{ParseAscii(name)}";
        }
        else
        {
            // We cannot fallback here due to original TAR required NULL terminated while "ustar" not.
            value = ParseAscii(name);
        }

        // Construct domain object.
        ItemName domain;

        try
        {
            domain = new(value);
        }
        catch (ArgumentException ex)
        {
            throw new ArchiveException("Invalid name.", ex);
        }

        return new(domain);

        static string ParseAscii(ReadOnlySpan<byte> value)
        {
            var end = value.IndexOf((byte)0);

            if (end == -1)
            {
                end = value.Length;
            }

            return Encoding.ASCII.GetString(value[..end]);
        }
    }

    protected override void WriteName(Span<byte> output)
    {
        Span<byte> value = stackalloc byte[256];
        var length = Encoding.ASCII.GetBytes(this.Name.ToString(), value);

        if (length > 255)
        {
            throw new ArchiveException("Name too long.");
        }
        else if (length > 100)
        {
            SplitName(value[..length], output);
        }
        else
        {
            // Name can fit in the original field.
            value[..length].CopyTo(output[..100]);

            if (length < 100)
            {
                output[length] = 0;
            }
        }

        static void SplitName(ReadOnlySpan<byte> value, Span<byte> output)
        {
            ReadOnlySpan<byte> prefix, name;

            // Check if we can split with 100 byte exactly as a last part.
            if (value[^101] == '/')
            {
                prefix = value[..^101];
                name = value[^100..];
            }
            else
            {
                // Find split point.
                var split = value[^100..].IndexOf((byte)'/');

                if (split == -1 || (split += value.Length - 100) > 155)
                {
                    throw new ArchiveException("Some parts of Name are too long.");
                }

                // Split value.
                prefix = value[..split++];
                name = value[split..];
            }

            // Write header.
            prefix.CopyTo(output[345..500]);
            name.CopyTo(output[..100]);

            // Add null terminate.
            if (prefix.Length < 155)
            {
                output[345 + prefix.Length] = 0;
            }

            if (name.Length < 100)
            {
                output[name.Length] = 0;
            }
        }
    }

    protected override void WriteMagic(Span<byte> output)
    {
        base.WriteMagic(output);

        output[262] = 0;
    }

    protected override void WriteVersion(Span<byte> output)
    {
        output[263] = (byte)'0';
        output[264] = (byte)'0';
    }
}
