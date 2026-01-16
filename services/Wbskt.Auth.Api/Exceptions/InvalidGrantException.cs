using System;

namespace Wbskt.Auth.Api.Exceptions
{
    public class InvalidGrantException : Exception
    {
        public InvalidGrantException(string message) : base(message)
        {
        }
    }
}