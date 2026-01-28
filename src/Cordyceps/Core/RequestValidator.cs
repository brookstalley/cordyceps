using System;
using System.IO;

namespace Cordyceps.Core
{
    /// <summary>
    /// Centralized request validation for MCP tool methods.
    /// Provides consistent validation patterns and error messages.
    /// </summary>
    public static class RequestValidator
    {
        #region String Validation

        /// <summary>
        /// Validate that a required string parameter is not null or empty.
        /// </summary>
        /// <param name="value">The string to validate</param>
        /// <param name="paramName">Parameter name for error message</param>
        /// <param name="error">Output error message if invalid</param>
        /// <returns>True if valid, false otherwise</returns>
        public static bool ValidateRequired(string value, string paramName, out string error)
        {
            if (string.IsNullOrEmpty(value))
            {
                error = $"{paramName} is required";
                return false;
            }
            error = null;
            return true;
        }

        /// <summary>
        /// Validate that a string is not null or whitespace.
        /// </summary>
        public static bool ValidateNotWhitespace(string value, string paramName, out string error)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                error = $"{paramName} cannot be empty or whitespace";
                return false;
            }
            error = null;
            return true;
        }

        #endregion

        #region GUID Validation

        /// <summary>
        /// Validate that a string is a valid GUID format.
        /// </summary>
        /// <param name="value">The string to validate</param>
        /// <param name="paramName">Parameter name for error message</param>
        /// <param name="guid">Output parsed GUID if valid</param>
        /// <param name="error">Output error message if invalid</param>
        /// <returns>True if valid, false otherwise</returns>
        public static bool ValidateGuid(string value, string paramName, out Guid guid, out string error)
        {
            guid = Guid.Empty;

            if (string.IsNullOrEmpty(value))
            {
                error = $"{paramName} is required";
                return false;
            }

            if (!Guid.TryParse(value, out guid))
            {
                error = $"Invalid {paramName} format - expected GUID";
                return false;
            }

            error = null;
            return true;
        }

        /// <summary>
        /// Validate that a string is a valid GUID format (without returning parsed GUID).
        /// </summary>
        public static bool ValidateGuidFormat(string value, string paramName, out string error)
        {
            return ValidateGuid(value, paramName, out _, out error);
        }

        #endregion

        #region Numeric Validation

        /// <summary>
        /// Validate that a numeric value is within a specified range (inclusive).
        /// </summary>
        public static bool ValidateRange(double value, double min, double max, string paramName, out string error)
        {
            if (value < min || value > max)
            {
                error = $"{paramName} must be between {min} and {max}";
                return false;
            }
            error = null;
            return true;
        }

        /// <summary>
        /// Validate that an integer value is within a specified range (inclusive).
        /// </summary>
        public static bool ValidateRange(int value, int min, int max, string paramName, out string error)
        {
            if (value < min || value > max)
            {
                error = $"{paramName} must be between {min} and {max}";
                return false;
            }
            error = null;
            return true;
        }

        /// <summary>
        /// Validate that a numeric value is positive (greater than zero).
        /// </summary>
        public static bool ValidatePositive(double value, string paramName, out string error)
        {
            if (value <= 0)
            {
                error = $"{paramName} must be positive";
                return false;
            }
            error = null;
            return true;
        }

        /// <summary>
        /// Validate that a numeric value is non-negative (zero or greater).
        /// </summary>
        public static bool ValidateNonNegative(double value, string paramName, out string error)
        {
            if (value < 0)
            {
                error = $"{paramName} cannot be negative";
                return false;
            }
            error = null;
            return true;
        }

        /// <summary>
        /// Validate that an integer is positive (greater than zero).
        /// </summary>
        public static bool ValidatePositive(int value, string paramName, out string error)
        {
            if (value <= 0)
            {
                error = $"{paramName} must be positive";
                return false;
            }
            error = null;
            return true;
        }

        #endregion

        #region File Path Validation

        /// <summary>
        /// Validate that a file path has one of the allowed extensions.
        /// </summary>
        /// <param name="filePath">The file path to validate</param>
        /// <param name="allowedExtensions">Allowed extensions including dot (e.g., ".gh", ".ghx")</param>
        /// <param name="error">Output error message if invalid</param>
        /// <returns>True if valid, false otherwise</returns>
        public static bool ValidateFileExtension(string filePath, string[] allowedExtensions, out string error)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                error = "File path is required";
                return false;
            }

            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            foreach (var allowed in allowedExtensions)
            {
                if (ext == allowed.ToLowerInvariant())
                {
                    error = null;
                    return true;
                }
            }

            error = $"Invalid file extension. Allowed: {string.Join(", ", allowedExtensions)}";
            return false;
        }

        /// <summary>
        /// Validate that a file exists at the specified path.
        /// </summary>
        public static bool ValidateFileExists(string filePath, out string error)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                error = "File path is required";
                return false;
            }

            if (!File.Exists(filePath))
            {
                error = $"File not found: {filePath}";
                return false;
            }

            error = null;
            return true;
        }

        /// <summary>
        /// Validate that a directory exists at the specified path.
        /// </summary>
        public static bool ValidateDirectoryExists(string dirPath, out string error)
        {
            if (string.IsNullOrEmpty(dirPath))
            {
                error = "Directory path is required";
                return false;
            }

            if (!Directory.Exists(dirPath))
            {
                error = $"Directory not found: {dirPath}";
                return false;
            }

            error = null;
            return true;
        }

        #endregion

        #region Enum Validation

        /// <summary>
        /// Validate that a string value is one of the allowed options (case-insensitive).
        /// </summary>
        /// <param name="value">The value to validate</param>
        /// <param name="allowedValues">Array of allowed values</param>
        /// <param name="paramName">Parameter name for error message</param>
        /// <param name="error">Output error message if invalid</param>
        /// <returns>True if valid, false otherwise</returns>
        public static bool ValidateOneOf(string value, string[] allowedValues, string paramName, out string error)
        {
            if (string.IsNullOrEmpty(value))
            {
                error = $"{paramName} is required";
                return false;
            }

            foreach (var allowed in allowedValues)
            {
                if (string.Equals(value, allowed, StringComparison.OrdinalIgnoreCase))
                {
                    error = null;
                    return true;
                }
            }

            error = $"Invalid {paramName}. Allowed values: {string.Join(", ", allowedValues)}";
            return false;
        }

        #endregion
    }
}
