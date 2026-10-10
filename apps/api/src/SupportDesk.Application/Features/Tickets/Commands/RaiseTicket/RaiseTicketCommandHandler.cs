using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Exceptions;
using SupportDesk.Application.Features.Tickets.Queries.GetTicket;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.Repositories;

namespace SupportDesk.Application.Features.Tickets.Commands.RaiseTicket;

/// <summary>
/// Raises a ticket and automatically determines its priority, SLA and owner.
/// </summary>
public sealed class RaiseTicketCommandHandler(
    ITicketRepository tickets,
    ICustomerRepository customers,
    ICategoryRepository categories,
    IAgentRepository agents,
    IUnitOfWork unitOfWork,
    IClock clock,
    IOptions<SlaOptions> slaOptions,
    GetTicketQueryHandler getTicket,
    ILogger<RaiseTicketCommandHandler> logger)
{
    public async Task<TicketDetailDto> HandleAsync(
        CreateTicketRequest request,
        CancellationToken ct)
    {
        var customer = await customers.GetByIdAsync(request.CustomerId, ct);

        if (customer is null)
        {
            throw new NotFoundException("Customer", request.CustomerId);
        }

        var category = await categories.GetByIdAsync(request.CategoryId, ct);

        if (category is null)
        {
            throw new NotFoundException("Category", request.CategoryId);
        }

        // BR-1: Use the requested priority, defaulting to Medium.
        // Categories configured to force Critical priority override that choice.
        var priority = category.ForcesCriticalPriority
            ? TicketPriority.Critical
            : request.RequestedPriority ?? TicketPriority.Medium;

        var priorityReason = category.ForcesCriticalPriority
            ? $"Priority is Critical because category '{category.Name}' requires it."
            : request.RequestedPriority.HasValue
                ? $"Priority is {priority}, as requested."
                : "Priority defaults to Medium because no priority was requested.";

        var now = clock.UtcNow;

        // BR-2, BR-3 and BR-6: Calculate the SLA using configuration.
        var options = slaOptions.Value;

        if (!options.BaseWindowHours.TryGetValue(
                priority.ToString(),
                out var baseWindowHours))
        {
            throw new InvalidOperationException(
                $"No SLA window is configured for priority '{priority}'.");
        }

        var windowHours = (double)baseWindowHours;

        var slaWindowReason =
            $"The base SLA for {priority} priority is {baseWindowHours} hours.";

        if (customer.Tier == CustomerTier.Premium)
        {
            windowHours *= options.PremiumCustomerMultiplier;
            slaWindowReason +=
                $" The Premium customer multiplier reduces it to {windowHours:0.##} hours.";
        }

        if (windowHours < options.MinimumWindowHours)
        {
            windowHours = options.MinimumWindowHours;
            slaWindowReason +=
                $" The minimum SLA window is {options.MinimumWindowHours:0.##} hour(s).";
        }

        var dueAtUtc = now.AddHours(windowHours);

        var ticket = Ticket.Raise(
            await tickets.NextReferenceAsync(ct),
            request.Title,
            request.Description,
            request.CustomerId,
            request.CategoryId,
            priority,
            now);

        ticket.SetSlaWindow(now, dueAtUtc);

        // BR-4 and BR-5: Find active, qualified agents with available capacity.
        var allAgents = await agents.GetAllAsync(ct);

        var eligibleAgents = new List<(Agent Agent, int OpenTicketCount)>();

        foreach (var agent in allAgents)
        {
            if (!agent.IsActive)
            {
                continue;
            }

            if (category.RequiresSpecialist &&
                !agent.Specializations.Any(
                    specialization => specialization.CategoryId == category.Id))
            {
                continue;
            }

            var openTicketCount = await tickets.CountOpenByAgentAsync(
                agent.Id,
                ct);

            if (openTicketCount < agent.MaxOpenTickets)
            {
                eligibleAgents.Add((agent, openTicketCount));
            }
        }

        // Select the least-loaded agent; use agent ID to break ties consistently.
        var assignedAgent = eligibleAgents
            .OrderBy(candidate => candidate.OpenTicketCount)
            .ThenBy(candidate => candidate.Agent.Id)
            .Select(candidate => candidate.Agent)
            .FirstOrDefault();

        string assignmentReason;

        if (assignedAgent is not null)
        {
            ticket.AssignTo(assignedAgent.Id, now);

            assignmentReason = category.RequiresSpecialist
                ? $"Assigned to {assignedAgent.FullName}, who is qualified for category '{category.Name}' and has available capacity."
                : $"Assigned to {assignedAgent.FullName}, the eligible agent with the fewest open tickets.";
        }
        else
        {
            assignmentReason = category.RequiresSpecialist
                ? $"No active specialist for category '{category.Name}' has available capacity. The ticket remains unassigned."
                : "No active agent has available capacity. The ticket remains unassigned.";
        }

        await tickets.AddAsync(ticket, ct);
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "Ticket {Reference} raised for customer {CustomerId} in category {CategoryId}. Priority: {Priority}, assigned agent: {AgentId}.",
            ticket.Reference,
            ticket.CustomerId,
            ticket.CategoryId,
            ticket.Priority,
            ticket.AssignedAgentId);

        var result = await getTicket.HandleAsync(ticket.Id, ct);

        result.Triage = new TicketTriageDto
        {
            PriorityReason = priorityReason,
            SlaWindowReason = slaWindowReason,
            AssignmentReason = assignmentReason
        };

        return result;
    }
}
