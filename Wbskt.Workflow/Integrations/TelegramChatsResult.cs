using Wbskt.Workflow.Engine.Contracts;

namespace Wbskt.Workflow.Integrations;

/// <summary>What asking Telegram for a bot's recent chats came to: the outcome, and the chats when it worked.</summary>
internal sealed record TelegramChatsResult(ChannelResult Result, IReadOnlyList<TelegramChat> Chats);
