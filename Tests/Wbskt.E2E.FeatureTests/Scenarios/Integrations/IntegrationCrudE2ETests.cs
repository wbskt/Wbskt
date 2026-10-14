using System.Net;
using System.Text.Json;
using FluentAssertions;
using Wbskt.E2E.FeatureTests.Fixtures;

namespace Wbskt.E2E.FeatureTests.Scenarios.Integrations;

/// <summary>
/// Saving, reading, changing and deleting integrations: every type's shape and defaults, that a
/// secret is never echoed, the validation and conflict answers, and who may see or change them.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class IntegrationCrudE2ETests(ServicesFixture fixture)
{
    private readonly IntegrationsApi _api = new(fixture);

    [SkippableFact]
    public async Task INT_CRUD_01_EachType_IsSaved_WithItsCategoryAndDefaults_AndNoSecretEchoed()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        var (token, ws) = await _api.NewWorkspaceAsync();

        const string discordUrl = "http://127.0.0.1:9/api/webhooks/1234/discord-webhook-secret-wxyz";
        var cases = new (object Body, string Type, string Category, string Secret, string Hint)[]
        {
            (IntegrationsApi.Telegram("Garage bot"), "telegram", "chat", IntegrationsApi.BotToken, "••••" + IntegrationsApi.BotToken[^4..]),
            (IntegrationsApi.Discord("Ops channel", discordUrl), "discord", "chat", discordUrl, "••••wxyz"),
            (IntegrationsApi.Smtp("Mail relay", 2525), "smtp", "email", "relay-password-1234", "••••1234"),
            (IntegrationsApi.Ntfy("Phone", "http://127.0.0.1:9", topic: "garage-door-alerts"), "ntfy", "push", "garage-door-alerts", "••••erts"),
            (IntegrationsApi.Bearer("Status API", "http://127.0.0.1:9/api"), "httpAuth", "http", "bearer-token-abcdef123456", "••••3456"),
        };

        foreach (var (body, type, category, secret, hint) in cases)
        {
            using var response = await _api.CreateRawAsync(token, ws, body);
            string text = await response.Content.ReadAsStringAsync();

            response.StatusCode.Should().Be(HttpStatusCode.Created, text);
            response.Headers.Location.Should().NotBeNull("a create names where the new integration lives");
            text.Should().NotContain(secret, "a saved secret never comes back from the API");

            var created = JsonDocument.Parse(text).RootElement;
            created.GetProperty("type").GetString().Should().Be(type);
            created.GetProperty("category").GetString().Should().Be(category);
            created.GetProperty("secretHint").GetString().Should().Be(hint);
            created.GetProperty("usage").GetProperty("workflows").GetInt32().Should().Be(0);
            created.GetProperty("lastTest").ValueKind.Should().Be(JsonValueKind.Null, "it has never been tested");
            created.GetProperty("createdBy").GetProperty("userRef").ValueKind.Should().Be(JsonValueKind.String);

            // The Location points at the same integration.
            using var located = await fixture.SendAsync(HttpMethod.Get, response.Headers.Location!.ToString(), token);
            located.StatusCode.Should().Be(HttpStatusCode.OK);
            IntegrationsApi.RefOf(await IntegrationsApi.ReadAsync(located)).Should().Be(IntegrationsApi.RefOf(created));
        }

        var smtp = (await _api.ListAsync(token, ws, "smtp")).GetProperty("items")[0];
        smtp.GetProperty("config").GetProperty("tls").GetString().Should().Be("startTls", "STARTTLS is the default off port 465");
        smtp.GetProperty("config").GetProperty("fromAddress").GetString().Should().Be("alerts@wbskt.test");

        var http = (await _api.ListAsync(token, ws, "httpAuth")).GetProperty("items")[0];
        http.GetProperty("config").GetProperty("baseUrl").GetString().Should().Be("http://127.0.0.1:9/api");
    }

    [SkippableFact]
    public async Task INT_CRUD_02_HomeAssistantPreset_IsBearerHttpAuth_AndRefusesOtherModes()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        var (token, ws) = await _api.NewWorkspaceAsync();

        var created = await _api.CreateAsync(token, ws, IntegrationsApi.HomeAssistant("House", "http://127.0.0.1:8123/"));
        created.GetProperty("type").GetString().Should().Be("httpAuth");
        created.GetProperty("preset").GetString().Should().Be("homeAssistant");
        created.GetProperty("config").GetProperty("mode").GetString().Should().Be("bearer", "Home Assistant takes a long-lived token as a bearer token");
        created.GetProperty("config").GetProperty("baseUrl").GetString().Should().Be("http://127.0.0.1:8123", "a trailing slash is trimmed");

        using var basic = await _api.CreateRawAsync(token, ws, new
        {
            type = "httpAuth",
            preset = "homeAssistant",
            name = "House basic",
            config = new { baseUrl = "http://127.0.0.1:8123", mode = "basic", userName = "me" },
            secrets = new { password = "pw-123456789012" }
        });
        basic.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ServicesFixture.ReadErrorCodeAsync(basic)).Should().Be("INTEGRATION_CONFIG_INVALID");

        using var wrongType = await _api.CreateRawAsync(token, ws, new { type = "telegram", preset = "homeAssistant", name = "Nope", secrets = new { botToken = IntegrationsApi.BotToken } });
        wrongType.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ServicesFixture.ReadErrorCodeAsync(wrongType)).Should().Be("INTEGRATION_PRESET_INVALID");
    }

    [SkippableFact]
    public async Task INT_CRUD_03_List_IsSortedByName_AndFiltersByType()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        var (token, ws) = await _api.NewWorkspaceAsync();

        await _api.CreateAsync(token, ws, IntegrationsApi.Telegram("Zulu bot"));
        await _api.CreateAsync(token, ws, IntegrationsApi.Bearer("Alpha API", "http://127.0.0.1:9"));
        await _api.CreateAsync(token, ws, IntegrationsApi.Telegram("Mike bot"));

        var all = await _api.ListAsync(token, ws);
        all.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("name").GetString())
            .Should().Equal("Alpha API", "Mike bot", "Zulu bot");
        all.GetProperty("totalCount").GetInt32().Should().Be(3);
        all.GetProperty("nextCursor").ValueKind.Should().Be(JsonValueKind.Null, "the list is one page");

        var telegram = await _api.ListAsync(token, ws, "telegram");
        telegram.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("name").GetString())
            .Should().Equal("Mike bot", "Zulu bot");

        using var badFilter = await fixture.SendAsync(HttpMethod.Get, IntegrationsApi.Url(ws, "?type=fax"), token);
        badFilter.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ServicesFixture.ReadErrorCodeAsync(badFilter)).Should().Be("INTEGRATION_TYPE_INVALID");
    }

    [SkippableFact]
    public async Task INT_CRUD_04_Rename_ChangesOnlyTheName_AndKeepsTheSecret()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        var (token, ws) = await _api.NewWorkspaceAsync();
        var created = await _api.CreateAsync(token, ws, IntegrationsApi.Telegram("Old name"));
        var refId = IntegrationsApi.RefOf(created);

        var renamed = await _api.UpdateAsync(token, ws, refId, new { name = "  New name  " });

        renamed.GetProperty("name").GetString().Should().Be("New name", "names are trimmed");
        renamed.GetProperty("secretHint").GetString().Should().Be(created.GetProperty("secretHint").GetString());
        renamed.GetProperty("secretRotatedAt").ValueKind.Should().Be(JsonValueKind.Null, "the secret was not touched");
        renamed.GetProperty("updatedAt").GetDateTime().Should().BeOnOrAfter(created.GetProperty("updatedAt").GetDateTime());
    }

    [SkippableFact]
    public async Task INT_CRUD_05_RotatingTheSecret_ChangesTheHint_AndStampsTheRotation()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        var (token, ws) = await _api.NewWorkspaceAsync();
        var created = await _api.CreateAsync(token, ws, IntegrationsApi.Bearer("API", "http://127.0.0.1:9", token: "first-token-aaaa1111"));
        var refId = IntegrationsApi.RefOf(created);

        using var response = await _api.UpdateRawAsync(token, ws, refId, new { secrets = new { token = "second-token-bbbb2222" } });
        string text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, text);
        text.Should().NotContain("second-token-bbbb2222");

        var rotated = JsonDocument.Parse(text).RootElement;
        rotated.GetProperty("secretHint").GetString().Should().Be("••••2222");
        rotated.GetProperty("secretRotatedAt").ValueKind.Should().Be(JsonValueKind.String);
        rotated.GetProperty("config").GetProperty("baseUrl").GetString().Should().Be("http://127.0.0.1:9", "a secrets-only change keeps the settings");

        // The same value again is not a rotation.
        var again = await _api.UpdateAsync(token, ws, refId, new { secrets = new { token = "second-token-bbbb2222" } });
        again.GetProperty("secretRotatedAt").GetDateTime().Should().Be(rotated.GetProperty("secretRotatedAt").GetDateTime());
    }

    [SkippableFact]
    public async Task INT_CRUD_06_ChangingSettingsOnTheSameServer_KeepsTheSecret_AndAPartialSecretPatchKeepsTheRest()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        var (token, ws) = await _api.NewWorkspaceAsync();
        await using var server = FakeHttpServer.Start();

        var api = IntegrationsApi.RefOf(await _api.CreateAsync(token, ws, IntegrationsApi.Bearer("API", server.BaseUrl + "/v1", token: "kept-token-abcd1234")));
        var moved = await _api.UpdateAsync(token, ws, api, new { config = new { baseUrl = server.BaseUrl + "/v2", mode = "bearer" } });
        moved.GetProperty("config").GetProperty("baseUrl").GetString().Should().Be(server.BaseUrl + "/v2");
        moved.GetProperty("secretHint").GetString().Should().Be("••••1234", "a new path on the same origin keeps the credential");
        moved.GetProperty("secretRotatedAt").ValueKind.Should().Be(JsonValueKind.Null);

        // Patching the topic alone keeps the access token: the test below proves it is still sent.
        var phone = IntegrationsApi.RefOf(await _api.CreateAsync(token, ws, IntegrationsApi.Ntfy("Phone", server.BaseUrl, topic: "first-topic-abcd", accessToken: "tk_access_token_1234")));
        var patched = await _api.UpdateAsync(token, ws, phone, new { secrets = new { topic = "second-topic-wxyz" } });
        patched.GetProperty("secretHint").GetString().Should().Be("••••wxyz");

        var test = await _api.TestAsync(token, ws, new { refId = phone });
        test.GetProperty("ok").GetBoolean().Should().BeTrue(test.ToString());
        var published = server.Requests.Should().ContainSingle().Subject;
        published.Header("Authorization").Should().Be("Bearer tk_access_token_1234");
        published.Body.Should().Contain("second-topic-wxyz");
    }

    [SkippableFact]
    public async Task INT_CRUD_07_TheTypeCannotChange()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        var (token, ws) = await _api.NewWorkspaceAsync();
        var refId = IntegrationsApi.RefOf(await _api.CreateAsync(token, ws, IntegrationsApi.Telegram("Bot")));

        using var response = await _api.UpdateRawAsync(token, ws, refId, new { type = "discord" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("INTEGRATION_TYPE_IMMUTABLE");

        // Naming the type it already has is fine.
        (await _api.UpdateAsync(token, ws, refId, new { type = "telegram", name = "Bot 2" }))
            .GetProperty("name").GetString().Should().Be("Bot 2");
    }

    [SkippableFact]
    public async Task INT_CRUD_08_NamesAreUniquePerWorkspace_NotAcrossWorkspaces()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        var (token, ws) = await _api.NewWorkspaceAsync();
        await _api.CreateAsync(token, ws, IntegrationsApi.Telegram("Shared name"));
        var other = IntegrationsApi.RefOf(await _api.CreateAsync(token, ws, IntegrationsApi.Telegram("Other name")));

        using var duplicate = await _api.CreateRawAsync(token, ws, IntegrationsApi.Telegram("Shared name"));
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ServicesFixture.ReadErrorCodeAsync(duplicate)).Should().Be("INTEGRATION_NAME_TAKEN");

        using var renamedOnto = await _api.UpdateRawAsync(token, ws, other, new { name = "Shared name" });
        renamedOnto.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ServicesFixture.ReadErrorCodeAsync(renamedOnto)).Should().Be("INTEGRATION_NAME_TAKEN");

        var secondWorkspace = await fixture.CreateWorkspaceAsync(token);
        await _api.CreateAsync(token, secondWorkspace, IntegrationsApi.Telegram("Shared name"));
    }

    public static TheoryData<string, object> InvalidBodies => new()
    {
        { "INTEGRATION_NAME_INVALID", new { type = "telegram", name = "  ", secrets = new { botToken = IntegrationsApi.BotToken } } },
        { "INTEGRATION_NAME_INVALID", new { type = "telegram", name = new string('n', 101), secrets = new { botToken = IntegrationsApi.BotToken } } },
        { "INTEGRATION_TYPE_INVALID", new { type = "fax", name = "Fax", secrets = new { } } },
        { "INTEGRATION_PRESET_INVALID", new { type = "httpAuth", preset = "fridge", name = "Fridge", config = new { baseUrl = "http://127.0.0.1:9", mode = "bearer" }, secrets = new { token = "t" } } },
        { "INTEGRATION_SECRET_INVALID", new { type = "telegram", name = "No token", secrets = new { } } },
        { "INTEGRATION_SECRET_INVALID", new { type = "telegram", name = "Bad token", secrets = new { botToken = "not-a-token" } } },
        { "INTEGRATION_SECRET_INVALID", new { type = "telegram", name = "Stray secret", secrets = new { botToken = IntegrationsApi.BotToken, password = "x" } } },
        { "INTEGRATION_CONFIG_INVALID", new { type = "telegram", name = "Stray setting", config = new { chatId = "1" }, secrets = new { botToken = IntegrationsApi.BotToken } } },
        { "INTEGRATION_SECRET_INVALID", new { type = "discord", name = "Not discord", secrets = new { webhookUrl = "https://example.com/api/webhooks/1/x" } } },
        { "INTEGRATION_CONFIG_INVALID", new { type = "smtp", name = "No host", config = new { fromAddress = "a@b.test" } } },
        { "INTEGRATION_CONFIG_INVALID", new { type = "smtp", name = "No from", config = new { host = "127.0.0.1" } } },
        { "INTEGRATION_CONFIG_INVALID", new { type = "smtp", name = "Bad tls", config = new { host = "127.0.0.1", tls = "ssl3", fromAddress = "a@b.test" } } },
        { "INTEGRATION_SECRET_INVALID", new { type = "smtp", name = "User no password", config = new { host = "127.0.0.1", userName = "u", fromAddress = "a@b.test" } } },
        { "INTEGRATION_SECRET_INVALID", new { type = "ntfy", name = "Bad topic", config = new { serverUrl = "http://127.0.0.1:9" }, secrets = new { topic = "has spaces" } } },
        { "INTEGRATION_CONFIG_INVALID", new { type = "ntfy", name = "Query", config = new { serverUrl = "http://127.0.0.1:9/?x=1" }, secrets = new { topic = "t" } } },
        { "INTEGRATION_CONFIG_INVALID", new { type = "httpAuth", name = "No base", config = new { mode = "bearer" }, secrets = new { token = "t" } } },
        { "INTEGRATION_CONFIG_INVALID", new { type = "httpAuth", name = "Bad mode", config = new { baseUrl = "http://127.0.0.1:9", mode = "digest" }, secrets = new { token = "t" } } },
        { "INTEGRATION_CONFIG_INVALID", new { type = "httpAuth", name = "Reserved header", config = new { baseUrl = "http://127.0.0.1:9", mode = "header", headerName = "Cookie" }, secrets = new { headerValue = "v" } } },
        { "INTEGRATION_CONFIG_INVALID", new { type = "httpAuth", name = "Basic no user", config = new { baseUrl = "http://127.0.0.1:9", mode = "basic" }, secrets = new { password = "p" } } },
        { "INTEGRATION_SECRET_INVALID", new { type = "httpAuth", name = "Bearer no token", config = new { baseUrl = "http://127.0.0.1:9", mode = "bearer" }, secrets = new { } } },
    };

    [SkippableTheory]
    [MemberData(nameof(InvalidBodies))]
    public async Task INT_CRUD_09_InvalidIntegrations_AreRefusedWithTheirCode(string code, object body)
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        var (token, ws) = await _api.NewWorkspaceAsync();

        using var response = await _api.CreateRawAsync(token, ws, body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be(code);
    }

    [SkippableFact]
    public async Task INT_CRUD_10_AnotherWorkspacesIntegration_IsNotFound_ForEveryVerb()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        var (ownerToken, ownerWs) = await _api.NewWorkspaceAsync();
        var refId = IntegrationsApi.RefOf(await _api.CreateAsync(ownerToken, ownerWs, IntegrationsApi.Telegram("Private bot")));

        // Another tenant entirely, and another workspace of the same owner.
        var (strangerToken, strangerWs) = await _api.NewWorkspaceAsync();
        var siblingWs = await fixture.CreateWorkspaceAsync(ownerToken);

        foreach (var (token, ws) in new[] { (strangerToken, strangerWs), (ownerToken, siblingWs) })
        {
            var responses = new[]
            {
                await _api.GetRawAsync(token, ws, refId),
                await _api.UpdateRawAsync(token, ws, refId, new { name = "Mine now" }),
                await _api.DeleteRawAsync(token, ws, refId),
                await fixture.SendAsync(HttpMethod.Get, IntegrationsApi.Url(ws, $"/{refId}/usage"), token),
                await _api.TestRawAsync(token, ws, new { refId }),
            };

            foreach (var response in responses)
            {
                response.StatusCode.Should().Be(HttpStatusCode.NotFound, response.RequestMessage!.RequestUri!.ToString());
                (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("INTEGRATION_NOT_FOUND");
                response.Dispose();
            }
        }

        // Still there, untouched, for its owner.
        (await _api.GetAsync(ownerToken, ownerWs, refId)).GetProperty("name").GetString().Should().Be("Private bot");
    }

    [SkippableFact]
    public async Task INT_CRUD_11_DeletingAnUnusedIntegration_RemovesIt()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        var (token, ws) = await _api.NewWorkspaceAsync();
        var refId = IntegrationsApi.RefOf(await _api.CreateAsync(token, ws, IntegrationsApi.Telegram("Short-lived")));

        using (var deleted = await _api.DeleteRawAsync(token, ws, refId))
        {
            deleted.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        using var gone = await _api.GetRawAsync(token, ws, refId);
        gone.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var again = await _api.DeleteRawAsync(token, ws, refId);
        again.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // The name is free again.
        await _api.CreateAsync(token, ws, IntegrationsApi.Telegram("Short-lived"));
    }

    [SkippableFact]
    public async Task INT_CRUD_12_AWorkspaceHoldsAtMostFifty()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        var (token, ws) = await _api.NewWorkspaceAsync();

        for (int i = 0; i < 50; i++)
        {
            using var created = await _api.CreateRawAsync(token, ws, IntegrationsApi.Bearer($"API {i:00}", "http://127.0.0.1:9"));
            created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        }

        using var oneTooMany = await _api.CreateRawAsync(token, ws, IntegrationsApi.Bearer("API 50", "http://127.0.0.1:9"));
        oneTooMany.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ServicesFixture.ReadErrorCodeAsync(oneTooMany)).Should().Be("INTEGRATION_LIMIT_REACHED");

        // The limit is per workspace.
        var secondWorkspace = await fixture.CreateWorkspaceAsync(token);
        await _api.CreateAsync(token, secondWorkspace, IntegrationsApi.Bearer("API 50", "http://127.0.0.1:9"));

        // Deleting one makes room.
        var first = (await _api.ListAsync(token, ws)).GetProperty("items")[0];
        (await _api.DeleteRawAsync(token, ws, IntegrationsApi.RefOf(first))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        await _api.CreateAsync(token, ws, IntegrationsApi.Bearer("API 50", "http://127.0.0.1:9"));
    }

    [SkippableFact]
    public async Task INT_CRUD_13_TheUserRole_CanRead_ButNotChange()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        var admin = await fixture.CreateUserAsync();
        var tenantRef = await fixture.GetTenantRefAsync(admin.Token);
        var ws = await fixture.CreateWorkspaceAsync(admin.Token);
        var refId = IntegrationsApi.RefOf(await _api.CreateAsync(admin.Token, ws, IntegrationsApi.Telegram("Team bot")));
        var userRole = (await fixture.GetRolesAsync(admin.Token, tenantRef)).Single(r => r.Name == "User");

        var (username, email, password) = ServicesFixture.NewCredentials();
        using (var invited = await fixture.SendAsync(
                   HttpMethod.Post,
                   ServicesFixture.AuthUrl($"/api/tenants/{tenantRef}/invitations"),
                   admin.Token,
                   new { Email = email, RoleRef = userRole.RefId, WorkspaceRefs = new[] { ws } }))
        {
            invited.EnsureSuccessStatusCode();
            var invitation = JsonDocument.Parse(await invited.Content.ReadAsStringAsync()).RootElement;
            (await fixture.RegisterAsync(username, email, password, invitation.GetProperty("token").GetString())).EnsureSuccessStatusCode();
        }

        var member = await fixture.LoginAsync(email, password);
        (await fixture.GetEffectivePermissionsAsync(member.AccessToken, ws))
            .Should().Contain("integrations.read").And.NotContain("integrations.manage");

        (await _api.ListAsync(member.AccessToken, ws)).GetProperty("items").GetArrayLength().Should().Be(1);
        (await _api.GetAsync(member.AccessToken, ws, refId)).GetProperty("name").GetString().Should().Be("Team bot");
        await _api.UsageAsync(member.AccessToken, ws, refId);

        var refused = new[]
        {
            await _api.CreateRawAsync(member.AccessToken, ws, IntegrationsApi.Telegram("Mine")),
            await _api.UpdateRawAsync(member.AccessToken, ws, refId, new { name = "Renamed" }),
            await _api.DeleteRawAsync(member.AccessToken, ws, refId),
            await _api.TestRawAsync(member.AccessToken, ws, new { refId }),
        };
        foreach (var response in refused)
        {
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden, response.RequestMessage!.Method + " " + response.RequestMessage.RequestUri);
            response.Dispose();
        }
    }

    [SkippableFact]
    public async Task INT_CRUD_14_WithoutSigningIn_NothingIsServed()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        var (_, ws) = await _api.NewWorkspaceAsync();

        using var list = await fixture.SendAsync(HttpMethod.Get, IntegrationsApi.Url(ws));
        list.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using var create = await fixture.SendAsync(HttpMethod.Post, IntegrationsApi.Url(ws), body: IntegrationsApi.Telegram("Anon"));
        create.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task INT_CRUD_15_PointingAnIntegrationAtAnotherServer_NeedsItsSecretAgain_AndNeverSendsTheSavedOne()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        var (token, ws) = await _api.NewWorkspaceAsync();
        await using var house = FakeHttpServer.Start();
        await using var elsewhere = FakeHttpServer.Start();
        var ha = IntegrationsApi.RefOf(await _api.CreateAsync(token, ws, IntegrationsApi.HomeAssistant("House", house.BaseUrl, token: "ha-saved-token-123456")));

        // Saving the move without the token is refused, and nothing changes.
        using (var move = await _api.UpdateRawAsync(token, ws, ha, new { config = new { baseUrl = elsewhere.BaseUrl } }))
        {
            move.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await ServicesFixture.ReadErrorCodeAsync(move)).Should().Be("INTEGRATION_SECRET_INVALID");
        }

        (await _api.GetAsync(token, ws, ha)).GetProperty("config").GetProperty("baseUrl").GetString().Should().Be(house.BaseUrl);

        // Neither is testing the saved token against the other server.
        using (var probe = await _api.TestRawAsync(token, ws, new { refId = ha, config = new { baseUrl = elsewhere.BaseUrl } }))
        {
            probe.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        elsewhere.Requests.Should().BeEmpty("the saved token never leaves for a server it was not saved for");

        // With the token given again, the move is saved as a new secret.
        var moved = await _api.UpdateAsync(token, ws, ha, new { config = new { baseUrl = elsewhere.BaseUrl }, secrets = new { token = "ha-new-token-654321" } });
        moved.GetProperty("secretRotatedAt").ValueKind.Should().Be(JsonValueKind.String);
        (await _api.TestAsync(token, ws, new { refId = ha })).GetProperty("ok").GetBoolean().Should().BeTrue();
        elsewhere.Requests.Should().ContainSingle().Which.Header("Authorization").Should().Be("Bearer ha-new-token-654321");

        // The same holds for a mail relay's host and port.
        await using var smtp = FakeSmtpServer.Start("mailer", "relay-password-1234");
        var relay = IntegrationsApi.RefOf(await _api.CreateAsync(token, ws, IntegrationsApi.Smtp("Relay", smtp.Port)));
        using var relayMove = await _api.UpdateRawAsync(token, ws, relay, new { config = new { host = "127.0.0.1", port = FakeHttpServer.FreePort(), userName = "mailer", fromAddress = "alerts@wbskt.test" } });
        relayMove.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ServicesFixture.ReadErrorCodeAsync(relayMove)).Should().Be("INTEGRATION_SECRET_INVALID");
    }
}
