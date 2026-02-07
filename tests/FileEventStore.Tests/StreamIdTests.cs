using Xunit;
using FileEventStore;

namespace FileEventStore.Tests;

public class StreamIdTests
{
    [Theory]
    [InlineData("valid-stream")]
    [InlineData("my_stream_123")]
    [InlineData("a")]
    [InlineData("stream.name")]
    [InlineData("UPPERCASE")]
    [InlineData("mixedCase123")]
    [InlineData("with-multiple-hyphens")]
    [InlineData("with_multiple_underscores")]
    public void From_accepts_valid_stream_ids(string id)
    {
        var streamId = StreamId.From(id);
        
        Assert.Equal(id, streamId.Value);
    }

    [Fact]
    public void From_rejects_null()
    {
        Assert.Throws<ArgumentException>(() => StreamId.From(null!));
    }

    [Fact]
    public void From_rejects_empty_string()
    {
        Assert.Throws<ArgumentException>(() => StreamId.From(""));
    }

    [Fact]
    public void From_rejects_whitespace()
    {
        Assert.Throws<ArgumentException>(() => StreamId.From("   "));
    }

    [Theory]
    [InlineData("../etc/passwd")]
    [InlineData("stream/../other")]
    [InlineData("..")]
    [InlineData("some..thing")]
    public void From_rejects_path_traversal(string id)
    {
        var ex = Assert.Throws<ArgumentException>(() => StreamId.From(id));
        Assert.Contains("..", ex.Message);
    }

    [Theory]
    [InlineData("stream/name")]
    [InlineData("stream\\name")]
    [InlineData("stream:name")]
    [InlineData("stream<name")]
    [InlineData("stream>name")]
    [InlineData("stream|name")]
    [InlineData("stream\"name")]
    [InlineData("stream*name")]
    [InlineData("stream?name")]
    public void From_rejects_invalid_filesystem_characters(string id)
    {
        Assert.Throws<ArgumentException>(() => StreamId.From(id));
    }

    [Theory]
    [InlineData(".hidden")]
    [InlineData("trailing.")]
    [InlineData(" leading-space")]
    [InlineData("trailing-space ")]
    public void From_rejects_leading_or_trailing_dots_and_spaces(string id)
    {
        Assert.Throws<ArgumentException>(() => StreamId.From(id));
    }

    [Fact]
    public void From_rejects_ids_over_200_characters()
    {
        var longId = new string('a', 201);
        
        var ex = Assert.Throws<ArgumentException>(() => StreamId.From(longId));
        Assert.Contains("200", ex.Message);
    }

    [Fact]
    public void From_accepts_id_at_200_characters()
    {
        var maxId = new string('a', 200);
        
        var streamId = StreamId.From(maxId);
        
        Assert.Equal(200, streamId.Value.Length);
    }

    [Fact]
    public void TryFrom_returns_true_for_valid_id()
    {
        var success = StreamId.TryFrom("valid-id", out var streamId);
        
        Assert.True(success);
        Assert.Equal("valid-id", streamId.Value);
    }

    [Fact]
    public void TryFrom_returns_false_for_invalid_id()
    {
        var success = StreamId.TryFrom("../invalid", out var streamId);
        
        Assert.False(success);
        Assert.Equal(default, streamId);
    }

    [Fact]
    public void Implicit_conversion_to_string()
    {
        var streamId = StreamId.From("my-stream");
        
        string value = streamId;
        
        Assert.Equal("my-stream", value);
    }

    [Fact]
    public void Equality_works_correctly()
    {
        var id1 = StreamId.From("same");
        var id2 = StreamId.From("same");
        var id3 = StreamId.From("different");

        Assert.Equal(id1, id2);
        Assert.NotEqual(id1, id3);
        Assert.True(id1 == id2);
        Assert.True(id1 != id3);
    }

    [Fact]
    public void ToString_returns_value()
    {
        var streamId = StreamId.From("test-stream");
        
        Assert.Equal("test-stream", streamId.ToString());
    }
    
    // ==========================================================================
    // IMPLICIT CONVERSION TESTS
    // ==========================================================================
    
    [Fact]
    public void Implicit_conversion_from_string_creates_valid_StreamId()
    {
        StreamId streamId = "my-stream-123";
        
        Assert.Equal("my-stream-123", streamId.Value);
    }
    
    [Fact]
    public void Implicit_conversion_from_string_validates()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            StreamId streamId = "../malicious";
        });
    }
    
    [Fact]
    public void Implicit_conversion_to_string_returns_value()
    {
        StreamId streamId = StreamId.From("test-stream");
        
        string value = streamId;
        
        Assert.Equal("test-stream", value);
    }
    
    [Fact]
    public void Can_pass_string_to_method_expecting_StreamId()
    {
        // Simulates the ergonomic API usage
        var result = AcceptStreamId("my-stream");
        
        Assert.Equal("my-stream", result);
    }
    
    private static string AcceptStreamId(StreamId id) => id.Value;
}
