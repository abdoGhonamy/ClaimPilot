using ClaimPilot.Domain.Enums;

namespace ClaimPilot.Infrastructure.Data.Seed;

public sealed record SectionSpec(string Title, string Clause, int Page, string Text);

public sealed record WordingSpec
{
    public required string ProductLine { get; init; }
    public required string PolicyNumber { get; init; }
    public required string PolicyName { get; init; }
    public required int Version { get; init; }
    public required DateTime EffectiveDate { get; init; }
    public string? SupersedesPolicyNumber { get; init; }
    public decimal? Deductible { get; init; }
    public decimal? Coinsurance { get; init; }
    public decimal? CoverageLimit { get; init; }
    public required IReadOnlyList<(string Code, string Name, string Description)> Exclusions { get; init; }
    public required IReadOnlyList<SectionSpec> Sections { get; init; }
}

/// <summary>
/// Synthetic corpus: 15 policy wordings across 5 product lines (AUTO, HEALTH,
/// HOME, TRAVEL, LIFE) with version traps, exclusions, and deterministic
/// coverage numbers. Used for seeding, evaluation, and acceptance tests.
/// </summary>
public static class CorpusSpec
{
    public static IReadOnlyList<WordingSpec> All { get; } = Build();

    private static IReadOnlyList<WordingSpec> Build() => new List<WordingSpec>
    {
        Auto2022V1(),
        Auto2022V2(),
        AutoPremium2019(),
        AutoCompact2020(),
        Health2019(),
        Health2023(),
        HealthFamily2021(),
        Home2018V1(),
        Home2018V2(),
        HomeStandard2020(),
        HomeHighValue2022(),
        TravelStandard2022(),
        TravelPremium2023(),
        TravelGroup2021(),
        LifeTerm2021(),
        LifeUniversal2019(),
        LifeAccident2020()
    };

    public static WordingSpec Auto2022V1() => new()
    {
        ProductLine = "AUTO",
        PolicyNumber = "AUT-2022",
        PolicyName = "DriveRight Collision & Comprehensive",
        Version = 1,
        EffectiveDate = new DateTime(2022, 1, 1),
        Deductible = 500m,
        Coinsurance = 1m,
        CoverageLimit = 5000m,
        Exclusions = new[]
        {
            ("EX-9", "Mechanical Breakdown", "Damage to the vehicle caused solely by mechanical or electrical " +
                "breakdown is not covered."),
            ("EX-14", "Racing", "Any vehicle used in a speed contest, time trial, or race is not covered."),
            ("EX-5", "Wear and Tear", "Wear and tear, depreciation, and normal maintenance are not covered.")
        },
        Sections = new[]
        {
            new SectionSpec("Limits of Liability", "Collision Coverage Limit", 4,
                "Under the 2022 wording, the maximum payable for a covered collision accident is $5,000 per " +
                "accident. This limit does NOT increase for newer wordings until a new version is issued."),
            new SectionSpec("Limits of Liability", "Comprehensive Coverage Limit", 5,
                "Comprehensive loss (theft, fire, vandalism, glass) is payable up to $3,000 per claim under " +
                "this wording."),
            new SectionSpec("Deductible", "Collision Deductible", 6,
                "A flat deductible of $500 applies to every collision claim. The deductible is subtracted " +
                "before the coverage limit is applied."),
            new SectionSpec("Deductible", "Comprehensive Deductible", 6,
                "Comprehensive claims carry a $250 deductible."),
            new SectionSpec("Coinsurance", "Coinurance Provision", 7,
                "There is no coinsurance for collision in this wording; the company pays the full covered " +
                "amount after the deductible."),
            new SectionSpec("Exclusions", "Standard Exclusions", 8,
                "Mechanical breakdown (EX-9), racing (EX-14), and wear and tear (EX-5) are excluded from " +
                "coverage under this auto wording."),
            new SectionSpec("Claims & Incident Reporting", "Reporting A Claim", 9,
                "You must report an accident within 30 days. A report does not waive the applicable deductible " +
                "or limit."),
            new SectionSpec("Territory", "Covered Territory", 9,
                "Coverage applies within the United States and Canada."),
            new SectionSpec("Cancellation & Renewal", "Renewal Terms", 10,
                "The policy renews annually on the anniversary date unless cancelled in writing."),
            new SectionSpec("Endorsements", "Rental Car Add-on", 11,
                "The rental car endorsement provides up to $30 per day for a rental while the vehicle is in " +
                "repair following a covered loss.")
        }
    };

    public static WordingSpec Auto2022V2() => new()
    {
        ProductLine = "AUTO",
        PolicyNumber = "AUT-2022",
        PolicyName = "DriveRight Collision & Comprehensive",
        Version = 2,
        EffectiveDate = new DateTime(2025, 6, 1),
        SupersedesPolicyNumber = "AUT-2022",
        Deductible = 600m,
        Coinsurance = 1m,
        CoverageLimit = 10000m,
        Exclusions = new[]
        {
            ("EX-9", "Mechanical Breakdown", "Damage caused solely by mechanical or electrical breakdown is not " +
                "covered."),
            ("EX-14", "Racing", "Vehicles used in speed contests are not covered."),
            ("EX-5", "Wear and Tear", "Wear and tear and maintenance are not covered."),
            ("EX-44", "Rideshare Use", "Losses that occur while the vehicle is used for rideshare or delivery " +
                "without endorsement are not covered.")
        },
        Sections = new[]
        {
            new SectionSpec("Limits of Liability", "Collision Coverage Limit", 4,
                "Effective with this 2025 renewal wording, the maximum payable for a covered collision accident " +
                "is $10,000 per accident. The prior 2022 wording capped collision at $5,000."),
            new SectionSpec("Limits of Liability", "Comprehensive Coverage Limit", 5,
                "Comprehensive loss is payable up to $5,000 per claim under this wording."),
            new SectionSpec("Deductible", "Collision Deductible", 6,
                "A deductible of $600 applies to each collision claim and is subtracted before the coverage " +
                "limit is applied."),
            new SectionSpec("Deductible", "Comprehensive Deductible", 6,
                "Comprehensive claims carry a $250 deductible."),
            new SectionSpec("Coinsurance", "Coinurance Provision", 7,
                "No coinsurance applies to collision under this wording; the company pays the covered amount " +
                "after the deductible."),
            new SectionSpec("Exclusions", "Standard Exclusions", 8,
                "Mechanical breakdown (EX-9), racing (EX-14), wear and tear (EX-5), and rideshare use without " +
                "endorsement (EX-44) are excluded."),
            new SectionSpec("Claims & Incident Reporting", "Reporting A Claim", 9,
                "Accidents must be reported within 30 days. Reporting does not waive the deductible or limit."),
            new SectionSpec("Territory", "Covered Territory", 9,
                "Coverage applies within the United States and Canada."),
            new SectionSpec("Cancellation & Renewal", "Renewal Terms", 10,
                "Renewals occur annually on the anniversary date."),
            new SectionSpec("Endorsements", "Rental Car Add-on", 11,
                "The rental car endorsement provides up to $40 per day following a covered loss.")
        }
    };

    public static WordingSpec AutoPremium2019() => new()
    {
        ProductLine = "AUTO",
        PolicyNumber = "AUT-PREM-2019",
        PolicyName = "DriveRight Premium Plus",
        Version = 1,
        EffectiveDate = new DateTime(2019, 3, 1),
        Deductible = 0m,
        Coinsurance = 1m,
        CoverageLimit = 25000m,
        Exclusions = new[]
        {
            ("EX-9", "Mechanical Breakdown", "Mechanical or electrical breakdown is excluded."),
            ("EX-31", "Intentional Damage", "Damage intentionally caused by the named insured is excluded.")
        },
        Sections = new[]
        {
            new SectionSpec("Limits of Liability", "Premium Collision Limit", 3,
                "Premium Plus collision coverage pays up to $25,000 per accident after a $0 deductible. " +
                "No deductible is collected on this wording."),
            new SectionSpec("Deductible", "Zero Deductible", 4,
                "This wording carries a $0 deductible for collision and comprehensive losses."),
            new SectionSpec("Coinsurance", "Coinurance Provision", 5,
                "No coinsurance applies; the full covered amount is paid."),
            new SectionSpec("Exclusions", "Exclusions", 6,
                "Mechanical breakdown (EX-9) and intentional damage (EX-31) are excluded."),
            new SectionSpec("Claims & Incident Reporting", "Reporting A Claim", 7,
                "Accidents must be reported within 30 days."),
            new SectionSpec("Coverage Trigger", "First Accident Favor", 8,
                "This wording includes one claim-forgiveness event per policy period following the first " +
                "reported at-fault claim.")
        }
    };

    public static WordingSpec AutoCompact2020() => new()
    {
        ProductLine = "AUTO",
        PolicyNumber = "AUT-COMPACT-2020",
        PolicyName = "CityRun Compact",
        Version = 1,
        EffectiveDate = new DateTime(2020, 5, 1),
        Deductible = 750m,
        Coinsurance = 1m,
        CoverageLimit = 4000m,
        Exclusions = new[]
        {
            ("EX-9", "Mechanical Breakdown", "Mechanical and electrical breakdown is excluded."),
            ("EX-5", "Wear and Tear", "Wear and tear is excluded.")
        },
        Sections = new[]
        {
            new SectionSpec("Limits of Liability", "Compact Collision Limit", 2,
                "CityRun compact collision pays up to $4,000 per accident with a $750 deductible."),
            new SectionSpec("Deductible", "Deductible", 3,
                "A $750 deductible applies to collision; $300 applies to comprehensive."),
            new SectionSpec("Coinsurance", "Coinurance Provision", 4,
                "No coinsurance applies."),
            new SectionSpec("Exclusions", "Exclusions", 5,
                "Mechanical breakdown (EX-9) and wear and tear (EX-5) are excluded."),
            new SectionSpec("Claims & Incident Reporting", "Reporting A Claim", 6,
                "Accidents must be reported within 14 days on compact coverages."),
            new SectionSpec("Coverage Trigger", "Roadside", 7,
                "Roadside assistance is included up to 3 events per year.")
        }
    };

    public static WordingSpec Health2019() => new()
    {
        ProductLine = "HEALTH",
        PolicyNumber = "HLT-2019",
        PolicyName = "CarePlus Basic",
        Version = 1,
        EffectiveDate = new DateTime(2019, 1, 1),
        Deductible = 300m,
        Coinsurance = 0.8m,
        CoverageLimit = 2000m,
        Exclusions = new[]
        {
            ("EX-51", "Cosmetic Surgery", "Elective cosmetic surgery is not covered."),
            ("EX-52", "Pre-existing Condition Waiting", "Conditions present within 6 months before enrollment " +
                "are excluded for the first 12 months of coverage.")
        },
        Sections = new[]
        {
            new SectionSpec("Limits of Liability", "Outpatient Cap", 5,
                "CarePlus Basic pays eligible outpatient expenses of up to $2,000 per calendar year."),
            new SectionSpec("Deductible", "Annual Deductible", 6,
                "A $300 annual deductible applies before the company pays any outpatient benefit."),
            new SectionSpec("Coinsurance", "Coinsurance 80/20", 7,
                "After the deductible, the company pays 80% of eligible expenses; the member pays 20%."),
            new SectionSpec("Exclusions", "Exclusions", 8,
                "Cosmetic surgery (EX-51) and pre-existing conditions within the waiting period (EX-52) are " +
                "excluded."),
            new SectionSpec("Claims & Incident Reporting", "Filing a Claim", 9,
                "Claims must be filed within 90 days of the date of service."),
            new SectionSpec("Coverage Trigger", "In-Network Requirement", 10,
                "The 80% payment applies only to in-network providers; out-of-network services pay at 50%."),
            new SectionSpec("Cancellation & Renewal", "Renewal", 11,
                "Coverage renews automatically each January 1 unless cancelled.")
        }
    };

    public static WordingSpec Health2023() => new()
    {
        ProductLine = "HEALTH",
        PolicyNumber = "HLT-2023",
        PolicyName = "CarePlus Standard",
        Version = 1,
        EffectiveDate = new DateTime(2023, 1, 1),
        Deductible = 500m,
        Coinsurance = 0.8m,
        CoverageLimit = 5000m,
        Exclusions = new[]
        {
            ("EX-51", "Cosmetic Surgery", "Elective cosmetic surgery is not covered."),
            ("EX-53", "Experimental Treatment", "Experimental or investigational treatments are not covered."),
            ("EX-54", "Weight Loss Programs", "Health club memberships and weight loss programs are not covered.")
        },
        Sections = new[]
        {
            new SectionSpec("Limits of Liability", "Outpatient Cap", 5,
                "CarePlus Standard pays eligible outpatient expenses up to $5,000 per calendar year."),
            new SectionSpec("Deductible", "Annual Deductible", 6,
                "A $500 annual deductible applies before benefits are paid."),
            new SectionSpec("Coinsurance", "Coinsurance 80/20", 7,
                "After the deductible, the company pays 80% and the member pays 20% of eligible expenses."),
            new SectionSpec("Exclusions", "Exclusions", 8,
                "Cosmetic surgery (EX-51), experimental treatment (EX-53), and weight loss programs (EX-54) " +
                "are excluded."),
            new SectionSpec("Claims & Incident Reporting", "Filing a Claim", 9,
                "Claims must be filed within 180 days of the date of service."),
            new SectionSpec("Coverage Trigger", "Preventive Care", 10,
                "Annual preventive exams are covered at 100% with no deductible.")
        }
    };

    public static WordingSpec HealthFamily2021() => new()
    {
        ProductLine = "HEALTH",
        PolicyNumber = "HLT-FAMILY-2021",
        PolicyName = "CarePlus Family",
        Version = 1,
        EffectiveDate = new DateTime(2021, 6, 1),
        Deductible = 1000m,
        Coinsurance = 0.9m,
        CoverageLimit = 8000m,
        Exclusions = new[]
        {
            ("EX-55", "Dental & Vision", "Routine dental and vision services are not covered."),
            ("EX-52", "Pre-existing Condition Waiting", "Pre-existing conditions are excluded for the first " +
                "12 months of coverage.")
        },
        Sections = new[]
        {
            new SectionSpec("Limits of Liability", "Family Outpatient Cap", 4,
                "CarePlus Family pays eligible outpatient expenses up to $8,000 per family per calendar year."),
            new SectionSpec("Deductible", "Family Deductible", 5,
                "A $1,000 family deductible applies per calendar year."),
            new SectionSpec("Coinsurance", "Coinsurance 90/10", 6,
                "After the deductible, the company pays 90% and the member pays 10%."),
            new SectionSpec("Exclusions", "Exclusions", 7,
                "Routine dental and vision (EX-55) and pre-existing conditions during the waiting period " +
                "(EX-52) are excluded."),
            new SectionSpec("Claims & Incident Reporting", "Filing a Claim", 8,
                "Claims must be filed within 90 days of service.")
        }
    };

    public static WordingSpec Home2018V1() => new()
    {
        ProductLine = "HOME",
        PolicyNumber = "HOM-2018",
        PolicyName = "SafeHaven Homeowners",
        Version = 1,
        EffectiveDate = new DateTime(2018, 1, 1),
        Deductible = 1000m,
        Coinsurance = 1m,
        CoverageLimit = 100000m,
        Exclusions = new[]
        {
            ("EX-70", "Flood", "Flood damage, defined as water entering from outside the dwelling caused by " +
                "rising water or overflow, is NOT covered."),
            ("EX-71", "Earthquake", "Earthquake damage is not covered."),
            ("EX-72", "Mold", "Mold, fungus, and wet rot are not covered."),
            ("EX-73", "Ordinance Violation", "Extra costs to comply with building ordinances are not covered.")
        },
        Sections = new[]
        {
            new SectionSpec("Limits of Liability", "Dwelling Limit", 3,
                "The dwelling is insured up to $100,000 replacement cost under this 2018 wording."),
            new SectionSpec("Deductible", "Deductible", 4,
                "A $1,000 deductible applies to property claims."),
            new SectionSpec("Coinsurance", "Coinurance Provision", 5,
                "No coinsurance applies to dwelling claims; payment is at replacement cost up to the limit."),
            new SectionSpec("Exclusions", "Flood Exclusion", 6,
                "EX-70 excludes flood damage. Water entering the dwelling from outside due to rising water, " +
                "storm surge, or overflow is NOT covered under this wording."),
            new SectionSpec("Exclusions", "Other Exclusions", 7,
                "Earthquake (EX-71), mold (EX-72), and ordinance violations (EX-73) are excluded."),
            new SectionSpec("Claims & Incident Reporting", "Reporting A Claim", 8,
                "Property claims must be reported within 1 year of the loss."),
            new SectionSpec("Coverage Trigger", "Replacement Cost", 9,
                "Claims adjust on a replacement cost basis; depreciation is recovered when repair is completed.")
        }
    };

    public static WordingSpec Home2018V2() => new()
    {
        ProductLine = "HOME",
        PolicyNumber = "HOM-2018",
        PolicyName = "SafeHaven Homeowners",
        Version = 2,
        EffectiveDate = new DateTime(2024, 1, 1),
        SupersedesPolicyNumber = "HOM-2018",
        Deductible = 1500m,
        Coinsurance = 1m,
        CoverageLimit = 120000m,
        Exclusions = new[]
        {
            ("EX-71", "Earthquake", "Earthquake damage is not covered."),
            ("EX-72", "Mold", "Mold, fungus, and wet rot are not covered."),
            ("EX-73", "Ordinance Violation", "Extra costs to comply with building ordinances are not covered."),
            ("EX-74", "Flood via Add-on Terms", "Flood damage is not covered unless the FloodGuard add-on " +
                "endorsement (FLOOD-ADDON) is attached to the policy.")
        },
        Sections = new[]
        {
            new SectionSpec("Limits of Liability", "Dwelling Limit", 3,
                "The dwelling is insured up to $120,000 replacement cost under the 2024 renewal wording."),
            new SectionSpec("Deductible", "Deductible", 4,
                "A $1,500 deductible applies to property claims under the 2024 wording."),
            new SectionSpec("Coinsurance", "Coinurance Provision", 5,
                "No coinsurance applies to dwelling claims."),
            new SectionSpec("FloodGuard Add-on", "Flood Coverage Endorsement", 6,
                "Flood damage is covered ONLY when the FloodGuard add-on endorsement (FLOOD-ADDON) is attached, " +
                "and is then payable up to $15,000 per event."),
            new SectionSpec("Exclusions", "Flood Exclusion", 6,
                "EX-74: flood damage is not covered unless the FloodGuard add-on is attached to the policy. " +
                "The prior 2018 wording excluded flood entirely."),
            new SectionSpec("Exclusions", "Other Exclusions", 7,
                "Earthquake (EX-71), mold (EX-72), and ordinance violations (EX-73) are excluded."),
            new SectionSpec("Claims & Incident Reporting", "Reporting A Claim", 8,
                "Property claims must be reported within 1 year of the loss.")
        }
    };

    public static WordingSpec HomeStandard2020() => new()
    {
        ProductLine = "HOME",
        PolicyNumber = "HOM-STD-2020",
        PolicyName = "SafeHaven Standard",
        Version = 1,
        EffectiveDate = new DateTime(2020, 4, 1),
        Deductible = 500m,
        Coinsurance = 1m,
        CoverageLimit = 50000m,
        Exclusions = new[]
        {
            ("EX-70", "Flood", "Flood damage is not covered."),
            ("EX-72", "Mold", "Mold, fungus, and wet rot are not covered.")
        },
        Sections = new[]
        {
            new SectionSpec("Limits of Liability", "Dwelling Limit", 3,
                "Standard homeowners coverage is written up to $50,000 replacement cost."),
            new SectionSpec("Deductible", "Deductible", 4,
                "A $500 deductible applies to property claims."),
            new SectionSpec("Coinsurance", "Coinurance Requirement", 5,
                "This wording requires the dwelling to be insured for at least 80% of its replacement cost; " +
                "coinsurance penalties may reduce payment otherwise."),
            new SectionSpec("Exclusions", "Exclusions", 6,
                "Flood (EX-70) and mold (EX-72) are excluded."),
            new SectionSpec("Claims & Incident Reporting", "Reporting A Claim", 8,
                "Property claims must be reported within 1 year of the loss.")
        }
    };

    public static WordingSpec HomeHighValue2022() => new()
    {
        ProductLine = "HOME",
        PolicyNumber = "HOM-HIGH-2022",
        PolicyName = "SafeHaven Signature",
        Version = 1,
        EffectiveDate = new DateTime(2022, 7, 1),
        Deductible = 2500m,
        Coinsurance = 1m,
        CoverageLimit = 500000m,
        Exclusions = new[]
        {
            ("EX-71", "Earthquake", "Earthquake damage is not covered."),
            ("EX-75", "Furs & Jewelry over Schedule", "Furs and jewelry are covered only up to the items " +
                "listed on the attached schedule, with a $10,000 per-item limit.")
        },
        Sections = new[]
        {
            new SectionSpec("Limits of Liability", "Signature Dwelling Limit", 3,
                "Signature coverage is written up to $500,000 replacement cost."),
            new SectionSpec("Deductible", "Deductible", 4,
                "A $2,500 deductible applies to property claims."),
            new SectionSpec("Coinsurance", "Coinurance Provision", 5,
                "No coinsurance applies; claims pay at replacement cost up to the limit."),
            new SectionSpec("Exclusions", "Exclusions", 6,
                "Earthquake (EX-71) and unscheduled furs/jewelry above the schedule limit (EX-75) are " +
                "excluded."),
            new SectionSpec("Claims & Incident Reporting", "Reporting A Claim", 8,
                "Claims must be reported within 1 year of the loss.")
        }
    };

    public static WordingSpec TravelStandard2022() => new()
    {
        ProductLine = "TRAVEL",
        PolicyNumber = "TRV-STD-2022",
        PolicyName = "JourneyCare Standard",
        Version = 1,
        EffectiveDate = new DateTime(2022, 2, 1),
        Deductible = 0m,
        Coinsurance = 1m,
        CoverageLimit = 5000m,
        Exclusions = new[]
        {
            ("EX-90", "Pre-existing Condition", "Pre-existing medical conditions are excluded for trips booked " +
                "within 60 days of departure."),
            ("EX-91", "Adventure Sports", "Skydiving, mountaineering above 4,500 meters, and motorsports are " +
                "excluded."),
            ("EX-92", "Known Event", "Cancellations caused by events known to the traveler at the time of " +
                "booking are excluded.")
        },
        Sections = new[]
        {
            new SectionSpec("Limits of Liability", "Trip Cancellation Cap", 4,
                "Trip cancellation and interruption benefits are payable up to $5,000 per trip."),
            new SectionSpec("Deductible", "Deductible", 5,
                "No deductible applies to trip cancellation benefits."),
            new SectionSpec("Coinsurance", "Coinurance Provision", 6,
                "No coinsurance applies; covered trip costs are reimbursed in full up to the cap."),
            new SectionSpec("Exclusions", "Exclusions", 7,
                "Pre-existing conditions (EX-90), adventure sports (EX-91), and known events (EX-92) are " +
                "excluded."),
            new SectionSpec("Claims & Incident Reporting", "Filing a Claim", 8,
                "Cancellation claims must be filed within 21 days of the cancellation date with supporting " +
                "documents."),
            new SectionSpec("Coverage Trigger", "Qualifying Trip", 9,
                "A qualifying trip must include at least one prepaid, non-refundable component.")
        }
    };

    public static WordingSpec TravelPremium2023() => new()
    {
        ProductLine = "TRAVEL",
        PolicyNumber = "TRV-PREM-2023",
        PolicyName = "JourneyCare Premium",
        Version = 1,
        EffectiveDate = new DateTime(2023, 1, 1),
        Deductible = 0m,
        Coinsurance = 1m,
        CoverageLimit = 20000m,
        Exclusions = new[]
        {
            ("EX-91", "Adventure Sports", "Skydiving and motorsports are excluded."),
            ("EX-93", "Non-covered Countries", "Trips to countries under a government travel advisory level 4 " +
                "are excluded.")
        },
        Sections = new[]
        {
            new SectionSpec("Limits of Liability", "Premium Trip Cancellation Cap", 3,
                "Premium trip cancellation benefits are payable up to $20,000 per trip."),
            new SectionSpec("Deductible", "Deductible", 4,
                "No deductible applies to trip cancellation or interruption."),
            new SectionSpec("Coinsurance", "Coinurance Provision", 5,
                "No coinsurance applies."),
            new SectionSpec("Exclusions", "Exclusions", 6,
                "Adventure sports (EX-91) and trips to level-4 advisory countries (EX-93) are excluded."),
            new SectionSpec("Claims & Incident Reporting", "Filing a Claim", 7,
                "Cancellation claims must be filed within 21 days.")
        }
    };

    public static WordingSpec TravelGroup2021() => new()
    {
        ProductLine = "TRAVEL",
        PolicyNumber = "TRV-GROUP-2021",
        PolicyName = "JourneyCare Group",
        Version = 1,
        EffectiveDate = new DateTime(2021, 9, 1),
        Deductible = 250m,
        Coinsurance = 1m,
        CoverageLimit = 3000m,
        Exclusions = new[]
        {
            ("EX-90", "Pre-existing Condition", "Pre-existing conditions are excluded for trips booked within " +
                "60 days of departure."),
            ("EX-92", "Known Event", "Cancellations caused by known events are excluded.")
        },
        Sections = new[]
        {
            new SectionSpec("Limits of Liability", "Group Cancellation Cap", 4,
                "Group travel cancellation benefits are payable up to $3,000 per traveler per trip."),
            new SectionSpec("Deductible", "Per-Traveler Deductible", 5,
                "A $250 deductible applies per traveler per trip."),
            new SectionSpec("Coinsurance", "Coinurance Provision", 6,
                "No coinsurance applies."),
            new SectionSpec("Exclusions", "Exclusions", 7,
                "Pre-existing conditions (EX-90) and known events (EX-92) are excluded."),
            new SectionSpec("Claims & Incident Reporting", "Filing a Claim", 8,
                "Cancellation claims must be filed within 21 days of cancellation.")
        }
    };

    public static WordingSpec LifeTerm2021() => new()
    {
        ProductLine = "LIFE",
        PolicyNumber = "LIF-TERM-2021",
        PolicyName = "SecureTerm 20",
        Version = 1,
        EffectiveDate = new DateTime(2021, 1, 1),
        Deductible = null,
        Coinsurance = null,
        CoverageLimit = 250000m,
        Exclusions = new[]
        {
            ("EX-100", "Suicide Exclusion Period", "Suicide within the first 2 policy years is not covered."),
            ("EX-101", "Misrepresentation", "Misrepresentation of health history on the application voids " +
                "coverage.")
        },
        Sections = new[]
        {
            new SectionSpec("Limits of Liability", "Death Benefit", 3,
                "SecureTerm 20 pays a level death benefit of $250,000 for 20 years."),
            new SectionSpec("Coverage Trigger", "Term Length", 4,
                "Coverage remains in force for a 20-year term while premiums are paid."),
            new SectionSpec("Exclusions", "Exclusions", 5,
                "Suicide within the first 2 policy years (EX-100) and misrepresentation (EX-101) are not " +
                "covered."),
            new SectionSpec("Claims & Incident Reporting", "Filing a Claim", 6,
                "Beneficiaries must file the claim with a certified death certificate within 1 year."),
            new SectionSpec("Cancellation & Renewal", "Conversion Privilege", 7,
                "The policy may be converted to a permanent policy within the first 10 years without " +
                "additional underwriting.")
        }
    };

    public static WordingSpec LifeUniversal2019() => new()
    {
        ProductLine = "LIFE",
        PolicyNumber = "LIF-UL-2019",
        PolicyName = "FlexiLife Universal",
        Version = 1,
        EffectiveDate = new DateTime(2019, 10, 1),
        Deductible = null,
        Coinsurance = null,
        CoverageLimit = 400000m,
        Exclusions = new[]
        {
            ("EX-100", "Suicide Exclusion Period", "Suicide within the first 2 policy years is not covered.")
        },
        Sections = new[]
        {
            new SectionSpec("Limits of Liability", "Base Death Benefit", 4,
                "FlexiLife Universal provides a base death benefit of $400,000 that may increase with excess " +
                "contributions."),
            new SectionSpec("Coverage Trigger", "Cash Value Account", 5,
                "Premiums in excess of the cost of insurance fund a cash value account credited at a declared " +
                "rate."),
            new SectionSpec("Exclusions", "Exclusions", 6,
                "Suicide within the first 2 policy years (EX-100) is not covered."),
            new SectionSpec("Cancellation & Renewal", "Lapse Protection", 7,
                "Coverage lapses if the account value cannot cover the monthly cost of insurance.")
        }
    };

    public static WordingSpec LifeAccident2020() => new()
    {
        ProductLine = "LIFE",
        PolicyNumber = "LIF-ACCIDENT-2020",
        PolicyName = "Guardian Accident",
        Version = 1,
        EffectiveDate = new DateTime(2020, 8, 1),
        Deductible = null,
        Coinsurance = null,
        CoverageLimit = 100000m,
        Exclusions = new[]
        {
            ("EX-102", "Intoxication", "Losses caused while the insured was intoxicated are not covered."),
            ("EX-103", "Military Combat", "Losses while serving in military combat are not covered.")
        },
        Sections = new[]
        {
            new SectionSpec("Limits of Liability", "Accidental Death Benefit", 3,
                "Guardian Accident pays $100,000 for accidental death."),
            new SectionSpec("Coverage Trigger", "Accidental Injury Definition", 4,
                "An accidental injury is a bodily injury caused by a sudden, unexpected, external event."),
            new SectionSpec("Exclusions", "Exclusions", 5,
                "Losses caused by intoxication (EX-102) or military combat (EX-103) are not covered."),
            new SectionSpec("Claims & Incident Reporting", "Filing a Claim", 6,
                "Claims must be filed within 90 days of the loss.")
        }
    };
}