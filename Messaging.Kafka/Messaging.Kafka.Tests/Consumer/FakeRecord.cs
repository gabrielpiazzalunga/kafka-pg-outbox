using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace Messaging.Kafka.Tests.Consumer;

/// <summary>Minimal IMessage implementation satisfying the T : class, IMessage&lt;T&gt;, new() constraint.</summary>
public sealed class FakeRecord : IMessage<FakeRecord>
{
    public static readonly MessageParser<FakeRecord> Parser = new(() => new FakeRecord());

    public MessageDescriptor Descriptor => throw new NotSupportedException();
    public FakeRecord Clone() => new();
    public bool Equals(FakeRecord? other) => ReferenceEquals(this, other);
    public void MergeFrom(FakeRecord message) { }
    public void MergeFrom(CodedInputStream input) { }
    public void WriteTo(CodedOutputStream output) { }
    public int CalculateSize() => 0;
}
