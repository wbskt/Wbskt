using System.ComponentModel.DataAnnotations;
using System.Security.Authentication;

namespace Wbskt.Common.Exceptions;

public static class WbsktExceptions
{
    public static UnauthorizedAccessException EmailIdExists(string emailId)
    {
        return new UnauthorizedAccessException($"A user with the email address '{emailId}' already exists. Please use a different email or log in.");
    }

    public static ValidationException ChannelExists(string channelName)
    {
        return new ValidationException($"A channel named '{channelName}' already exists. Please choose a unique channel name.");
    }

    public static InvalidOperationException PublisherIdNotExists(int publisherId)
    {
        return new InvalidOperationException($"No publisher found with the internal ID '{publisherId}'. Please verify the publisher reference.");
    }

    public static InvalidOperationException PublisherRefNotExists(Guid publisherRef)
    {
        return new InvalidOperationException($"No publisher found with the reference '{publisherRef}'. Please check the publisher reference ID.");
    }

    public static InvalidOperationException ChannelIdNotExists(int channelId)
    {
        return new InvalidOperationException($"No channel found with the internal ID '{channelId}'. Please verify the channel reference.");
    }

    public static InvalidOperationException InvalidId(int id, string resource)
    {
        return new InvalidOperationException($"The provided ID '{id}' is invalid for the resource '{resource}'. Please check your request parameters.");
    }

    public static InvalidOperationException ChannelRefNotExists(Guid channelRef)
    {
        return new InvalidOperationException($"No channel found with the reference '{channelRef}'. Please check the channel reference ID.");
    }

    public static InvalidOperationException ClientIdNotExists(int clientId)
    {
        return new InvalidOperationException($"No client found with the internal ID '{clientId}'. Please verify the client reference.");
    }

    public static InvalidOperationException ClientRefNotExists(Guid clientRef)
    {
        return new InvalidOperationException($"No client found with the reference '{clientRef}'. Please check the client reference ID.");
    }

    public static InvalidOperationException UnknownSocketServer(int id)
    {
        return new InvalidOperationException($"Socket server with ID '{id}' is not registered or available. Please check the server status or configuration.");
    }

    public static ValidationException ClientWithSameNameExists(string reqClientName)
    {
        return new ValidationException($"A client named '{reqClientName}' already exists in this channel. Please use a unique client name.");
    }

    public static UnauthorizedAccessException UnauthorizedAccessToChannels()
    {
        return new UnauthorizedAccessException("You do not have permission to subscribe to one or more of the requested channels. Please check your access rights.");
    }

    public static InvalidOperationException SocketServerUnavailable()
    {
        return new InvalidOperationException("No socket servers are currently registered or available. Please try again later or contact support.");
    }

    public static InvalidOperationException FailedToInsertOrUpdateClient(Guid clientUniqueId)
    {
        return new InvalidOperationException($"An unexpected error occurred while trying to insert or update the client with unique ID '{clientUniqueId}'. Please try again or contact support.");
    }

    public static ArgumentException PolicyNameRequired(string paramName)
    {
        return new ArgumentException("The policy name is required and cannot be empty.", paramName);
    }

    public static ArgumentException ExpiryDateRequired(string paramName, string policyType)
    {
        return new ArgumentException($"An expiry date is required for '{policyType}' policies. Please provide a valid expiry date.", paramName);
    }

    public static ArgumentException ExpiryDateMustBeFuture(string paramName)
    {
        return new ArgumentException("The expiry date must be set to a future date and time.", paramName);
    }

    public static ArgumentException MaxClientsMustBeGreaterThanZero(string paramName, string policyType)
    {
        return new ArgumentException($"The maximum number of clients for '{policyType}' policies must be greater than zero.", paramName);
    }

    public static ArgumentException InvalidPolicyType(string paramName)
    {
        return new ArgumentException("The specified policy type is invalid. Please use a supported policy type.", paramName);
    }

    public static AuthenticationException UnableToGetClaim(string claimKey)
    {
        return new AuthenticationException($"Unable to retrieve the claim '{claimKey}' from the authentication context. Please ensure you are properly authenticated.");
    }

    public static InvalidOperationException ServerAddressNotInitialized()
    {
        return new InvalidOperationException("The server address feature is not initialized. Please ensure the server is configured correctly.");
    }

    public static InvalidOperationException PublisherWithNameExists(string publisherName)
    {
        return new InvalidOperationException($"A publisher named '{publisherName}' already exists for this user. Please use a unique publisher name.");
    }

    public static InvalidCredentialException UserNotFound(string emailId)
    {
        return new InvalidCredentialException($"A user with the email address '{emailId}' was not found. Please check the email address and try again.");
    }

    public static InvalidCredentialException InvalidCredentials()
    {
        return new InvalidCredentialException("The credentials provided are incorrect. Please check your email and password and try again.");
    }

    public static InvalidCredentialException InvalidToken()
    {
        return new InvalidCredentialException("The token provided is invalid or has expired. Please log in again to get a new token.");
    }

    public static InvalidOperationException PolicyNotFound(Guid refId)
    {
        return new InvalidOperationException($"A registration policy with the RefId '{refId}' was not found. Please check the policy RefId and try again.");
    }
}
