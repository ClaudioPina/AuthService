using System.Text.RegularExpressions;

namespace AuthService.Api.Utils
{
    public static class PasswordPolicy
    {
        public static (bool IsValid, string Error) Validate(string password)
        {
            if (password.Length < 8)
                return (false, "La contraseña debe tener al menos 8 caracteres.");

            if (!Regex.IsMatch(password, "[A-Z]"))
                return (false, "La contraseña debe contener al menos una letra mayúscula.");

            if (!Regex.IsMatch(password, "[a-z]"))
                return (false, "La contraseña debe contener al menos una letra minúscula.");

            if (!Regex.IsMatch(password, "[0-9]"))
                return (false, "La contraseña debe contener al menos un número.");

            if (!Regex.IsMatch(password, @"[\W_]"))
                return (false, "La contraseña debe contener al menos un símbolo.");

            return (true, string.Empty);
        }
    }
}
