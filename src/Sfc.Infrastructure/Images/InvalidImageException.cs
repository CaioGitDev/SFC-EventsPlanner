namespace Sfc.Infrastructure.Images;

public class InvalidImageException(string message, Exception? inner = null)
    : Exception(message, inner);
