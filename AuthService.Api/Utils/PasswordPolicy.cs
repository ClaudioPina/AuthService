namespace AuthService.Api.Utils
{
    public static class PasswordPolicy
    {
        // Lista explícita de símbolos aceptados. Más predecible que [\W_] y
        // permite mostrar al usuario exactamente qué caracteres son válidos.
        private static readonly char[] SpecialChars =
            "!@#$%^&*()-_=+[]{}|;:',.<>?/`~\\\"".ToCharArray();

        public static (bool IsValid, string Error) Validate(string password)
        {
            if (password.Length < 8)
                return (false, "La contraseña debe tener al menos 8 caracteres.");

            if (!password.Any(char.IsUpper))
                return (false, "La contraseña debe contener al menos una letra mayúscula.");

            if (!password.Any(char.IsLower))
                return (false, "La contraseña debe contener al menos una letra minúscula.");

            if (!password.Any(char.IsDigit))
                return (false, "La contraseña debe contener al menos un número.");

            if (!password.Any(c => SpecialChars.Contains(c)))
                return (false, "La contraseña debe contener al menos un símbolo (!@#$%^&* y otros).");

            return (true, string.Empty);
        }
    }
}
