using AuthService.Api.Utils;
using FluentAssertions;

namespace AuthService.Tests.Unit
{
    /// <summary>
    /// Tests unitarios para PasswordPolicy.
    /// Patrón Arrange/Act/Assert:
    ///   Arrange = preparar datos de entrada
    ///   Act = llamar al método
    ///   Assert = verificar el resultado esperado
    /// </summary>
    public class PasswordPolicyTests
    {
        [Fact]
        public void Validate_WithValidPassword_ReturnsSuccess()
        {
            // Arrange
            var password = "Abcdef1!";

            // Act
            var (isValid, error) = PasswordPolicy.Validate(password);

            // Assert
            isValid.Should().BeTrue();
            error.Should().BeNullOrEmpty();
        }

        [Fact]
        public void Validate_TooShort_ReturnsFalse()
        {
            var (isValid, error) = PasswordPolicy.Validate("Ab1!");
            isValid.Should().BeFalse();
            error.Should().Contain("8 caracteres");
        }

        [Fact]
        public void Validate_NoUppercase_ReturnsFalse()
        {
            var (isValid, error) = PasswordPolicy.Validate("abcdef1!");
            isValid.Should().BeFalse();
            error.Should().Contain("mayúscula");
        }

        [Fact]
        public void Validate_NoLowercase_ReturnsFalse()
        {
            var (isValid, error) = PasswordPolicy.Validate("ABCDEF1!");
            isValid.Should().BeFalse();
            error.Should().Contain("minúscula");
        }

        [Fact]
        public void Validate_NoDigit_ReturnsFalse()
        {
            var (isValid, error) = PasswordPolicy.Validate("Abcdefg!");
            isValid.Should().BeFalse();
            error.Should().Contain("número");
        }

        [Fact]
        public void Validate_NoSymbol_ReturnsFalse()
        {
            var (isValid, error) = PasswordPolicy.Validate("Abcdef12");
            isValid.Should().BeFalse();
            error.Should().Contain("símbolo");
        }

        [Fact]
        public void Validate_ExactlySevenChars_ReturnsFalse()
        {
            // El límite es 8 caracteres. 7 debe fallar para verificar que el boundary es correcto.
            var (isValid, _) = PasswordPolicy.Validate("Abcde1!");
            isValid.Should().BeFalse();
        }
    }
}
