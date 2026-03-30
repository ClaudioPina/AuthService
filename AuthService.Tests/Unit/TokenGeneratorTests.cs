using AuthService.Api.Utils;
using FluentAssertions;

namespace AuthService.Tests.Unit
{
    public class TokenGeneratorTests
    {
        [Fact]
        public void GenerateToken_DefaultLength_Returns64HexChars()
        {
            // 32 bytes → 64 chars hex (cada byte = 2 chars hex)
            var token = TokenGenerator.GenerateToken(32);
            token.Should().HaveLength(64);
        }

        [Fact]
        public void GenerateToken_TwoCalls_ReturnDifferentTokens()
        {
            var t1 = TokenGenerator.GenerateToken(32);
            var t2 = TokenGenerator.GenerateToken(32);
            t1.Should().NotBe(t2);
        }

        [Fact]
        public void HashToken_SameInput_ReturnsSameHash()
        {
            // SHA-256 es determinístico: mismo input → mismo output
            var h1 = TokenGenerator.HashToken("abc123");
            var h2 = TokenGenerator.HashToken("abc123");
            h1.Should().Be(h2);
        }

        [Fact]
        public void HashToken_DifferentInputs_ReturnDifferentHashes()
        {
            var h1 = TokenGenerator.HashToken("abc123");
            var h2 = TokenGenerator.HashToken("abc124");
            h1.Should().NotBe(h2);
        }

        [Fact]
        public void HashToken_ResultIsLowercase64HexChars()
        {
            // SHA-256 = 32 bytes = 64 chars hex
            var hash = TokenGenerator.HashToken("test");
            hash.Should().HaveLength(64).And.MatchRegex("^[0-9a-f]+$");
        }
    }
}
