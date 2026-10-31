-- Ids for dbo.EventLogs and dbo.DeviceTraffic. Both tables draw from this one sequence so an id is
-- unique across them and orders them together: the event log and the client comms readers page
-- over both by id, with the same cursor the API has always handed out. The post-deployment script
-- moves it past the highest id already in either table.
CREATE SEQUENCE dbo.EventLogIds
    AS BIGINT
    START WITH 1
    INCREMENT BY 1
    CACHE 1000;
GO
