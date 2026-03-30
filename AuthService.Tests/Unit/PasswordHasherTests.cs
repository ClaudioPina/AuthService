using AuthService.Api.Utils;
using FluentAssertions;

namespace AuthService.Tests.Unit
{
    public class PasswordHasherTests
    {
        [Fact]
        public void HashPassword_ReturnsStringDifferentFromInput()
        {
            var hash = PasswordHasher.HashPassword("Abcdef1!");
            hash.Should().NotBe("Abcdef1!");
        }

        [Fact]
        public void HashPassword_TwoCallsSameInput_ReturnDifferentHashes()
        {
            // BCrypt incluye un salt aleatorio → cada hash es único
            var hash1 = PasswordHasher.HashPassword("Abcdef1!");
            var hash2 = PasswordHasher.HashPassword("Abcdef1!");
            hash1.Should().NotBe(hash2);
        }

        [Fact]
        public void VerifyPassword_CorrectPassword_ReturnsTrue()
        {
            var hash = PasswordHasher.HashPassword("Abcdef1!");
            PasswordHasher.VerifyPassword("Abcdef1!", hash).Should().BeTrue();
        }

        [Fact]
        public void VerifyPassword_WrongPassword_ReturnsFalse()
        {
            var hash = PasswordHasher.HashPassword("Abcdef1!");
            PasswordHasher.VerifyPassword("WrongPass1!", hash).Should().BeFalse();
        }
    }
}
