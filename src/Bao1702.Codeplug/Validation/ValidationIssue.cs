namespace Bao1702.Codeplug.Validation;

/// <summary>Severity level for a codeplug validation issue.</summary>
public enum ValidationSeverity
{
    Info,
    Warning,
    Error,
}

public sealed record ValidationIssue(
    ValidationSeverity Severity,
    string Message,
    int? RowNumber = null,
    string? ColumnName = null);

public static class CodeplugValidator
{
    public static IReadOnlyList<ValidationIssue> Validate(Model.CodeplugImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var issues = new List<ValidationIssue>();
        var channelNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var contactNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var zoneNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rxGroupNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var scanListNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var channel in image.Channels)
        {
            if (!channelNames.Add(channel.Name))
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error, $"Channel name '{channel.Name}' is duplicated."));
            }

            if (channel.RxFrequencyHz <= 0 || channel.TxFrequencyHz <= 0)
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error, $"Channel '{channel.Name}' has a non-positive frequency."));
            }

            if (channel is Model.DigitalChannel digital)
            {
                if (digital.ColorCode is < 0 or > 15)
                {
                    issues.Add(new ValidationIssue(ValidationSeverity.Error, $"Channel '{channel.Name}' has an invalid color code."));
                }

                if (digital.TimeSlot is < 1 or > 2)
                {
                    issues.Add(new ValidationIssue(ValidationSeverity.Error, $"Channel '{channel.Name}' has an invalid time slot."));
                }
            }
        }

        foreach (var contact in image.Contacts)
        {
            if (!contactNames.Add(contact.Name))
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error, $"Contact name '{contact.Name}' is duplicated."));
            }

            if (contact.CallId <= 0)
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error, $"Contact '{contact.Name}' has a non-positive call ID."));
            }
        }

        foreach (var zone in image.Zones)
        {
            if (!zoneNames.Add(zone.Name))
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error, $"Zone name '{zone.Name}' is duplicated."));
            }

            foreach (var channelName in zone.ChannelNames)
            {
                if (!channelNames.Contains(channelName))
                {
                    issues.Add(new ValidationIssue(ValidationSeverity.Error, $"Zone '{zone.Name}' references unknown channel '{channelName}'."));
                }
            }
        }

        foreach (var rxGroup in image.RxGroups)
        {
            if (!rxGroupNames.Add(rxGroup.Name))
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error, $"RX group name '{rxGroup.Name}' is duplicated."));
            }

            foreach (var contactName in rxGroup.ContactNames)
            {
                if (!contactNames.Contains(contactName))
                {
                    issues.Add(new ValidationIssue(ValidationSeverity.Error, $"RX group '{rxGroup.Name}' references unknown contact '{contactName}'."));
                }
            }
        }

        var groupListNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var groupList in image.GroupLists)
        {
            if (!groupListNames.Add(groupList.Name))
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error, $"Group list name '{groupList.Name}' is duplicated."));
            }

            foreach (var contactName in groupList.ContactNames)
            {
                if (!contactNames.Contains(contactName))
                {
                    issues.Add(new ValidationIssue(ValidationSeverity.Error, $"Group list '{groupList.Name}' references unknown contact '{contactName}'."));
                }
            }
        }

        foreach (var scanList in image.ScanLists)
        {
            if (!scanListNames.Add(scanList.Name))
            {
                // Downgraded to warning: the OEM CPS and radio firmware permit duplicate
                // scan list names, so this must not block write-back of data read from the radio.
                issues.Add(new ValidationIssue(ValidationSeverity.Warning, $"Scan list name '{scanList.Name}' is duplicated."));
            }

            foreach (var channelName in scanList.ChannelNames)
            {
                if (!channelNames.Contains(channelName))
                {
                    issues.Add(new ValidationIssue(ValidationSeverity.Error, $"Scan list '{scanList.Name}' references unknown channel '{channelName}'."));
                }
            }
        }

        foreach (var channel in image.Channels.OfType<Model.DigitalChannel>())
        {
            if (channel.ContactName is not null && !contactNames.Contains(channel.ContactName))
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error, $"Digital channel '{channel.Name}' references unknown contact '{channel.ContactName}'."));
            }

            if (channel.RxGroupName is not null && !rxGroupNames.Contains(channel.RxGroupName))
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Error, $"Digital channel '{channel.Name}' references unknown RX group '{channel.RxGroupName}'."));
            }
        }

        return issues;
    }
}
