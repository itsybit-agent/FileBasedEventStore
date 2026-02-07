using Microsoft.Extensions.DependencyInjection;
using FileEventStore;
using FileEventStore.Aggregates;
using FileEventStore.Session;

// =============================================================================
// Session Sample: Demonstrates Unit of Work pattern with multiple aggregates
// =============================================================================

var services = new ServiceCollection();
services.AddFileEventStore(Path.Combine(Directory.GetCurrentDirectory(), "data"));

var provider = services.BuildServiceProvider();
var sessionFactory = provider.GetRequiredService<IEventSessionFactory>();

Console.WriteLine("=== FileEventStore Session Sample ===\n");

// Scenario: User creates a household, generates an invite, another user joins
var householdId = Guid.NewGuid().ToString();
var inviteCode = "INV-" + Guid.NewGuid().ToString()[..8].ToUpper();
var creatorId = "user-alice";
var joinerId = "user-bob";

// Step 1: Create a household
Console.WriteLine("Step 1: Creating household...");
await CreateHousehold(sessionFactory, householdId, "The Smiths", creatorId);
Console.WriteLine($"  ✓ Household '{householdId}' created by {creatorId}\n");

// Step 2: Generate an invite
Console.WriteLine("Step 2: Generating invite...");
await GenerateInvite(sessionFactory, householdId, inviteCode, creatorId);
Console.WriteLine($"  ✓ Invite code: {inviteCode}\n");

// Step 3: Join household (modifies TWO aggregates atomically)
Console.WriteLine("Step 3: Joining household (multi-aggregate operation)...");
await JoinHousehold(sessionFactory, inviteCode, joinerId, "Bob");
Console.WriteLine($"  ✓ {joinerId} joined the household\n");

// Step 4: Verify final state
Console.WriteLine("Step 4: Verifying final state...");
await VerifyState(sessionFactory, householdId, inviteCode);

Console.WriteLine("\n=== Sample Complete ===");


// =============================================================================
// Handler Functions (simulating your application's command handlers)
// =============================================================================

async Task CreateHousehold(IEventSessionFactory factory, string id, string name, string userId)
{
    await using var session = factory.OpenSession();
    
    var household = await session.AggregateStreamOrCreateAsync<HouseholdAggregate>(id);
    household.Create(id, name, userId);
    
    await session.SaveChangesAsync();
}

async Task GenerateInvite(IEventSessionFactory factory, string householdId, string code, string userId)
{
    await using var session = factory.OpenSession();
    
    // Load household to verify it exists
    var household = await session.AggregateStreamAsync<HouseholdAggregate>(householdId)
        ?? throw new InvalidOperationException("Household not found");
    
    // Create invite
    var invite = await session.AggregateStreamOrCreateAsync<InviteAggregate>(code);
    invite.Generate(code, householdId, userId);
    
    await session.SaveChangesAsync();
}

async Task JoinHousehold(IEventSessionFactory factory, string inviteCode, string userId, string displayName)
{
    // This is where Session shines: we modify TWO aggregates
    // and commit them together
    
    await using var session = factory.OpenSession();
    
    // Load invite
    var invite = await session.AggregateStreamAsync<InviteAggregate>(inviteCode)
        ?? throw new InvalidOperationException("Invalid invite code");
    
    if (invite.IsUsed)
        throw new InvalidOperationException("Invite already used");
    
    // Load household
    var household = await session.AggregateStreamAsync<HouseholdAggregate>(invite.HouseholdId)
        ?? throw new InvalidOperationException("Household not found");
    
    // Make changes to both aggregates
    invite.MarkUsed(userId);
    household.AddMember(userId, displayName);
    
    // Commit both changes together
    // If either fails, neither is saved (within the limits of file-based storage)
    await session.SaveChangesAsync();
}

async Task VerifyState(IEventSessionFactory factory, string householdId, string inviteCode)
{
    await using var session = factory.OpenSession();
    
    var household = await session.AggregateStreamAsync<HouseholdAggregate>(householdId);
    var invite = await session.AggregateStreamAsync<InviteAggregate>(inviteCode);
    
    Console.WriteLine($"  Household: {household?.Name}");
    Console.WriteLine($"  Members: {string.Join(", ", household?.Members ?? [])}");
    Console.WriteLine($"  Invite used: {invite?.IsUsed} (by {invite?.UsedByUserId ?? "n/a"})");
}


// =============================================================================
// Events
// =============================================================================

public record HouseholdCreated(
    string HouseholdId,
    string Name,
    string CreatorId,
    DateTime CreatedAt
) : IStoreableEvent
{
    public string TimestampUtc { get; set; } = "";
}

public record MemberJoined(
    string HouseholdId,
    string UserId,
    string DisplayName,
    DateTime JoinedAt
) : IStoreableEvent
{
    public string TimestampUtc { get; set; } = "";
}

public record InviteGenerated(
    string InviteCode,
    string HouseholdId,
    string GeneratedByUserId,
    DateTime GeneratedAt
) : IStoreableEvent
{
    public string TimestampUtc { get; set; } = "";
}

public record InviteUsed(
    string InviteCode,
    string UsedByUserId,
    DateTime UsedAt
) : IStoreableEvent
{
    public string TimestampUtc { get; set; } = "";
}


// =============================================================================
// Aggregates
// =============================================================================

public class HouseholdAggregate : Aggregate
{
    public string Name { get; private set; } = "";
    public List<string> Members { get; private set; } = new();

    public void Create(string id, string name, string creatorId)
    {
        if (!string.IsNullOrEmpty(Id))
            throw new InvalidOperationException("Household already exists");

        Emit(new HouseholdCreated(id, name, creatorId, DateTime.UtcNow));
    }

    public void AddMember(string userId, string displayName)
    {
        if (Members.Contains(userId))
            throw new InvalidOperationException("Already a member");

        Emit(new MemberJoined(Id, userId, displayName, DateTime.UtcNow));
    }

    protected override void Apply(IStoreableEvent evt)
    {
        switch (evt)
        {
            case HouseholdCreated e:
                Id = e.HouseholdId;
                Name = e.Name;
                Members.Add(e.CreatorId);
                break;
            case MemberJoined e:
                Members.Add(e.UserId);
                break;
        }
    }
}

public class InviteAggregate : Aggregate
{
    public string HouseholdId { get; private set; } = "";
    public bool IsUsed { get; private set; }
    public string? UsedByUserId { get; private set; }

    public void Generate(string code, string householdId, string generatedBy)
    {
        if (!string.IsNullOrEmpty(Id))
            throw new InvalidOperationException("Invite already exists");

        Emit(new InviteGenerated(code, householdId, generatedBy, DateTime.UtcNow));
    }

    public void MarkUsed(string userId)
    {
        if (IsUsed)
            throw new InvalidOperationException("Invite already used");

        Emit(new InviteUsed(Id, userId, DateTime.UtcNow));
    }

    protected override void Apply(IStoreableEvent evt)
    {
        switch (evt)
        {
            case InviteGenerated e:
                Id = e.InviteCode;
                HouseholdId = e.HouseholdId;
                break;
            case InviteUsed e:
                IsUsed = true;
                UsedByUserId = e.UsedByUserId;
                break;
        }
    }
}
