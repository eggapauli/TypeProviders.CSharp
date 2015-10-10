using System;

namespace TypeProviders.CSharp
{
    class NotifyUserException : Exception
    {
        public NotifyUserException()
        {
        }

        public NotifyUserException(string message)
            : base(message)
        {
        }

        public NotifyUserException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}