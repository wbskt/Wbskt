using Microsoft.Extensions.Logging;
using Wbskt.Common.Readers;
using Wbskt.Common.Records;
using Wbskt.Common.Writers;
using Wbskt.Core.Service.Services;

namespace Wbskt.Core.Service.Services.Implementations;

public class PublishersService(ILogger<PublishersService> logger, IPublishersWriter publishersWriter, IPublishersReader publishersReader) : IPublishersService
{
    public int Create(PublisherRecord publisherRecord)
    {
        logger.LogDebug("Creating publisher with name: {Name}", publisherRecord.Name);
        
        // Check if user already has a publisher with the same name
        var existingPublishers = GetAllForUser(publisherRecord.UserId);
        if (existingPublishers.Any(p => p.Name == publisherRecord.Name))
        {
            throw new InvalidOperationException($"Publisher with name '{publisherRecord.Name}' already exists for this user");
        }

        var id = publishersWriter.InsertPublisher(publisherRecord);
        logger.LogDebug("Successfully created publisher with ID: {Id}", id);
        return id;
    }

    public IReadOnlyCollection<PublisherReadRecord> GetAllForUser(int userId)
    {
        return publishersReader.GetAllByUserId(userId);
    }

    public int GetPublisherIdByRef(Guid publisherRef)
    {
        var publisher = publishersReader.GetByPublisherRef(publisherRef);
        return publisher.Id;
    }

    public Guid GetPublisherRefById(int publisherId)
    {
        var publisher = publishersReader.GetById(publisherId);
        return publisher.PublisherRef;
    }

    public IReadOnlyCollection<int> GetPublisherIdsByRefs(Guid[] publisherRefs)
    {
        var publishers = publishersReader.GetAllByPublisherRefs(publisherRefs);
        return publishers.Select(p => p.Id).ToList().AsReadOnly();
    }

    public IReadOnlyCollection<Guid> GetPublisherRefsByIds(int[] publisherIds)
    {
        var publishers = publishersReader.GetAllByIds(publisherIds);
        return publishers.Select(p => p.PublisherRef).ToList().AsReadOnly();
    }
} 