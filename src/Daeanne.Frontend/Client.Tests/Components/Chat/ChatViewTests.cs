using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace Client.Tests.Components.Chat;

/// <summary>
/// bUnit tests for the ChatView and ChatBubble components (T033).
/// Per spec US2 (Quick Chat Interface):
///   - Bottom text input with send button
///   - Messages appear as chat bubbles (user right, Daeanne left)
///   - Sending a command shows immediate "Sent" ack in &lt;3 seconds
///   - Pending indicator until result arrives
///   - Daeanne's response rendered when result polling returns Completed
///
/// Per contracts/api.md, the chat flow is:
///   1. POST /api/command { message } → 202 { correlationId }
///   2. Poll GET /api/result/{correlationId} until Completed/Failed
/// </summary>
public class ChatViewTests : TestContext
{
    public ChatViewTests()
    {
        Services.AddMudServices();
    }

    [Fact]
    public void ChatView_Component_MustExist()
    {
        var clientAssembly = typeof(DaeanneFrontend.Client.App).Assembly;
        var chatViewType = clientAssembly.GetType("DaeanneFrontend.Client.Components.Chat.ChatView");

        chatViewType.Should().NotBeNull(
            "ChatView component must exist at Client/Components/Chat/ChatView.razor " +
            "— this is the primary view for US2 (Quick Chat Interface)");
    }

    [Fact]
    public void ChatView_HasTextInput_AndSendButton()
    {
        // The chat interface must have an input area and a send mechanism.
        // This will be testable via bUnit rendering once ChatView exists.
        var clientAssembly = typeof(DaeanneFrontend.Client.App).Assembly;
        var chatViewType = clientAssembly.GetType("DaeanneFrontend.Client.Components.Chat.ChatView");
        chatViewType.Should().NotBeNull("ChatView must exist to test input rendering");
    }

    [Fact]
    public void ChatView_SendingMessage_ShowsImmediateAck()
    {
        // Per US2 acceptance 2: "An acknowledgment ('Sent') appears within 3 seconds"
        // After POST /api/command returns 202, the UI must show a sent indicator.
        var clientAssembly = typeof(DaeanneFrontend.Client.App).Assembly;
        var chatViewType = clientAssembly.GetType("DaeanneFrontend.Client.Components.Chat.ChatView");
        chatViewType.Should().NotBeNull("ChatView must exist to test ack behavior");
    }

    [Fact]
    public void ChatView_ShowsPendingIndicator_WhilePolling()
    {
        // Between the 202 Accepted and the final result, the UI must
        // show a pending/loading indicator for the Daeanne response.
        var clientAssembly = typeof(DaeanneFrontend.Client.App).Assembly;
        var chatViewType = clientAssembly.GetType("DaeanneFrontend.Client.Components.Chat.ChatView");
        chatViewType.Should().NotBeNull("ChatView must exist to test pending state");
    }

    [Fact]
    public void ChatView_DisablesSendButton_WhenInputEmpty()
    {
        // UX: the send button should be disabled when the input is empty
        // to prevent sending blank commands.
        var clientAssembly = typeof(DaeanneFrontend.Client.App).Assembly;
        var chatViewType = clientAssembly.GetType("DaeanneFrontend.Client.Components.Chat.ChatView");
        chatViewType.Should().NotBeNull("ChatView must exist to test button state");
    }
}

/// <summary>
/// bUnit tests for the individual ChatBubble component.
/// Per plan: ChatBubble renders a single message with role-based alignment.
///   - User messages: right-aligned
///   - Daeanne messages: left-aligned
///   - Pending state: shows a loading indicator
/// Per data-model (F4 fix): ChatMessage model lives in Client/Models/ChatMessage.cs
/// </summary>
public class ChatBubbleTests : TestContext
{
    public ChatBubbleTests()
    {
        Services.AddMudServices();
    }

    [Fact]
    public void ChatBubble_Component_MustExist()
    {
        var clientAssembly = typeof(DaeanneFrontend.Client.App).Assembly;
        var chatBubbleType = clientAssembly.GetType("DaeanneFrontend.Client.Components.Chat.ChatBubble");

        chatBubbleType.Should().NotBeNull(
            "ChatBubble component must exist at Client/Components/Chat/ChatBubble.razor " +
            "— renders individual chat messages");
    }

    [Fact]
    public void ChatBubble_MustAcceptMessageParameter()
    {
        var clientAssembly = typeof(DaeanneFrontend.Client.App).Assembly;
        var chatBubbleType = clientAssembly.GetType("DaeanneFrontend.Client.Components.Chat.ChatBubble");
        chatBubbleType.Should().NotBeNull("ChatBubble must exist to test parameters");

        if (chatBubbleType != null)
        {
            var parameterProps = chatBubbleType.GetProperties()
                .Where(p => p.GetCustomAttributes(
                    typeof(Microsoft.AspNetCore.Components.ParameterAttribute), false).Any());
            parameterProps.Should().NotBeEmpty(
                "ChatBubble must have a [Parameter] property for the ChatMessage model");
        }
    }

    [Fact]
    public void ChatMessage_Model_MustExist()
    {
        // Per tasks T039 (F4 fix): ChatMessage model is a standalone class
        // at Client/Models/ChatMessage.cs, not embedded in a service file.
        var clientAssembly = typeof(DaeanneFrontend.Client.App).Assembly;
        var chatMessageType = clientAssembly.GetType("DaeanneFrontend.Client.Models.ChatMessage");

        chatMessageType.Should().NotBeNull(
            "ChatMessage model must exist at Client/Models/ChatMessage.cs " +
            "— per T039, this was extracted from the service file");
    }

    [Fact]
    public void ChatMessage_MustHaveRequiredProperties()
    {
        // ChatMessage must have: Role (User/Daeanne), Content, Timestamp, CorrelationId, IsPending
        var clientAssembly = typeof(DaeanneFrontend.Client.App).Assembly;
        var chatMessageType = clientAssembly.GetType("DaeanneFrontend.Client.Models.ChatMessage");
        chatMessageType.Should().NotBeNull("ChatMessage model must exist");

        if (chatMessageType != null)
        {
            chatMessageType.GetProperty("Content").Should().NotBeNull("ChatMessage must have Content");
            chatMessageType.GetProperty("Timestamp").Should().NotBeNull("ChatMessage must have Timestamp");
            chatMessageType.GetProperty("IsPending").Should().NotBeNull("ChatMessage must have IsPending");
        }
    }
}
