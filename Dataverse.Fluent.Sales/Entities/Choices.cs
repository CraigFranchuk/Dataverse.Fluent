namespace Dataverse.Fluent.Sales.Entities;

// The choice (option set) values of the Sales tables, as Dynamics 365 Sales ships them. An organisation can add its
// own status reasons and options: such a value is read into the enum as its number, with no named member, and is
// written by casting, for example (LeadStatus)100000001. States cannot be customised.

/// <summary>The state of an account (<c>statecode</c>).</summary>
public enum AccountState
{
    /// <summary>Active.</summary>
    Active = 0,

    /// <summary>Inactive.</summary>
    Inactive = 1
}

/// <summary>The standard status reasons of an account (<c>statuscode</c>).</summary>
public enum AccountStatus
{
    /// <summary>Active. The record is <see cref="AccountState.Active"/>.</summary>
    Active = 1,

    /// <summary>Inactive. The record is <see cref="AccountState.Inactive"/>.</summary>
    Inactive = 2
}

/// <summary>The state of a contact (<c>statecode</c>).</summary>
public enum ContactState
{
    /// <summary>Active.</summary>
    Active = 0,

    /// <summary>Inactive.</summary>
    Inactive = 1
}

/// <summary>The standard status reasons of a contact (<c>statuscode</c>).</summary>
public enum ContactStatus
{
    /// <summary>Active. The record is <see cref="ContactState.Active"/>.</summary>
    Active = 1,

    /// <summary>Inactive. The record is <see cref="ContactState.Inactive"/>.</summary>
    Inactive = 2
}

/// <summary>The state of a lead (<c>statecode</c>).</summary>
public enum LeadState
{
    /// <summary>Open.</summary>
    Open = 0,

    /// <summary>Qualified.</summary>
    Qualified = 1,

    /// <summary>Disqualified.</summary>
    Disqualified = 2
}

/// <summary>The standard status reasons of a lead (<c>statuscode</c>).</summary>
public enum LeadStatus
{
    /// <summary>New. The record is <see cref="LeadState.Open"/>.</summary>
    New = 1,

    /// <summary>Contacted. The record is <see cref="LeadState.Open"/>.</summary>
    Contacted = 2,

    /// <summary>Qualified. The record is <see cref="LeadState.Qualified"/>.</summary>
    Qualified = 3,

    /// <summary>Lost. The record is <see cref="LeadState.Disqualified"/>.</summary>
    Lost = 4,

    /// <summary>Cannot Contact. The record is <see cref="LeadState.Disqualified"/>.</summary>
    CannotContact = 5,

    /// <summary>No Longer Interested. The record is <see cref="LeadState.Disqualified"/>.</summary>
    NoLongerInterested = 6,

    /// <summary>Canceled. The record is <see cref="LeadState.Disqualified"/>.</summary>
    Canceled = 7
}

/// <summary>The state of an opportunity (<c>statecode</c>).</summary>
public enum OpportunityState
{
    /// <summary>Open.</summary>
    Open = 0,

    /// <summary>Won.</summary>
    Won = 1,

    /// <summary>Lost.</summary>
    Lost = 2
}

/// <summary>The standard status reasons of an opportunity (<c>statuscode</c>).</summary>
public enum OpportunityStatus
{
    /// <summary>In Progress. The record is <see cref="OpportunityState.Open"/>.</summary>
    InProgress = 1,

    /// <summary>On Hold. The record is <see cref="OpportunityState.Open"/>.</summary>
    OnHold = 2,

    /// <summary>Won. The record is <see cref="OpportunityState.Won"/>.</summary>
    Won = 3,

    /// <summary>Canceled. The record is <see cref="OpportunityState.Lost"/>.</summary>
    Canceled = 4,

    /// <summary>Out-Sold. The record is <see cref="OpportunityState.Lost"/>.</summary>
    OutSold = 5
}

/// <summary>The state of a quote (<c>statecode</c>).</summary>
public enum QuoteState
{
    /// <summary>Draft.</summary>
    Draft = 0,

    /// <summary>Active.</summary>
    Active = 1,

    /// <summary>Won.</summary>
    Won = 2,

    /// <summary>Closed.</summary>
    Closed = 3
}

/// <summary>The standard status reasons of a quote (<c>statuscode</c>).</summary>
public enum QuoteStatus
{
    /// <summary>In Progress. The record is <see cref="QuoteState.Draft"/>.</summary>
    InProgressDraft = 1,

    /// <summary>In Progress. The record is <see cref="QuoteState.Active"/>.</summary>
    InProgressActive = 2,

    /// <summary>Open. The record is <see cref="QuoteState.Active"/>.</summary>
    Open = 3,

    /// <summary>Won. The record is <see cref="QuoteState.Won"/>.</summary>
    Won = 4,

    /// <summary>Lost. The record is <see cref="QuoteState.Closed"/>.</summary>
    Lost = 5,

    /// <summary>Canceled. The record is <see cref="QuoteState.Closed"/>.</summary>
    Canceled = 6,

    /// <summary>Revised. The record is <see cref="QuoteState.Closed"/>.</summary>
    Revised = 7
}

/// <summary>The state of a case (<c>statecode</c>).</summary>
public enum IncidentState
{
    /// <summary>Active.</summary>
    Active = 0,

    /// <summary>Resolved.</summary>
    Resolved = 1,

    /// <summary>Canceled.</summary>
    Canceled = 2
}

/// <summary>The standard status reasons of a case (<c>statuscode</c>).</summary>
public enum IncidentStatus
{
    /// <summary>In Progress. The record is <see cref="IncidentState.Active"/>.</summary>
    InProgress = 1,

    /// <summary>On Hold. The record is <see cref="IncidentState.Active"/>.</summary>
    OnHold = 2,

    /// <summary>Waiting for Details. The record is <see cref="IncidentState.Active"/>.</summary>
    WaitingForDetails = 3,

    /// <summary>Researching. The record is <see cref="IncidentState.Active"/>.</summary>
    Researching = 4,

    /// <summary>Problem Solved. The record is <see cref="IncidentState.Resolved"/>.</summary>
    ProblemSolved = 5,

    /// <summary>Canceled. The record is <see cref="IncidentState.Canceled"/>.</summary>
    Canceled = 6,

    /// <summary>Information Provided. The record is <see cref="IncidentState.Resolved"/>.</summary>
    InformationProvided = 1000,

    /// <summary>Merged. The record is <see cref="IncidentState.Canceled"/>.</summary>
    Merged = 2000
}

/// <summary>The priority of a case (<c>prioritycode</c>).</summary>
public enum IncidentPriority
{
    /// <summary>High.</summary>
    High = 1,

    /// <summary>Normal.</summary>
    Normal = 2,

    /// <summary>Low.</summary>
    Low = 3
}

/// <summary>The standard origins of a case (<c>caseorigincode</c>).</summary>
public enum CaseOrigin
{
    /// <summary>Phone.</summary>
    Phone = 1,

    /// <summary>Email.</summary>
    Email = 2,

    /// <summary>Web.</summary>
    Web = 3,

    /// <summary>Facebook.</summary>
    Facebook = 2483,

    /// <summary>Twitter.</summary>
    Twitter = 3986,

    /// <summary>IoT.</summary>
    IoT = 700610000
}

/// <summary>The customer's satisfaction with a case (<c>customersatisfactioncode</c>).</summary>
public enum CustomerSatisfaction
{
    /// <summary>Very Dissatisfied.</summary>
    VeryDissatisfied = 1,

    /// <summary>Dissatisfied.</summary>
    Dissatisfied = 2,

    /// <summary>Neutral.</summary>
    Neutral = 3,

    /// <summary>Satisfied.</summary>
    Satisfied = 4,

    /// <summary>Very Satisfied.</summary>
    VerySatisfied = 5
}

/// <summary>The rating of an opportunity (<c>opportunityratingcode</c>).</summary>
public enum OpportunityRating
{
    /// <summary>Hot.</summary>
    Hot = 1,

    /// <summary>Warm.</summary>
    Warm = 2,

    /// <summary>Cold.</summary>
    Cold = 3
}

/// <summary>The standard sources of a lead (<c>leadsourcecode</c>).</summary>
public enum LeadSource
{
    /// <summary>Advertisement.</summary>
    Advertisement = 1,

    /// <summary>Employee Referral.</summary>
    EmployeeReferral = 2,

    /// <summary>External Referral.</summary>
    ExternalReferral = 3,

    /// <summary>Partner.</summary>
    Partner = 4,

    /// <summary>Public Relations.</summary>
    PublicRelations = 5,

    /// <summary>Seminar.</summary>
    Seminar = 6,

    /// <summary>Trade Show.</summary>
    TradeShow = 7,

    /// <summary>Web.</summary>
    Web = 8,

    /// <summary>Word of Mouth.</summary>
    WordOfMouth = 9,

    /// <summary>Other.</summary>
    Other = 10
}

/// <summary>When a lead expects to purchase (<c>purchasetimeframe</c>).</summary>
public enum PurchaseTimeFrame
{
    /// <summary>Immediate.</summary>
    Immediate = 0,

    /// <summary>This Quarter.</summary>
    ThisQuarter = 1,

    /// <summary>Next Quarter.</summary>
    NextQuarter = 2,

    /// <summary>This Year.</summary>
    ThisYear = 3,

    /// <summary>Unknown.</summary>
    Unknown = 4
}

/// <summary>The standard industries of an account or a lead (<c>industrycode</c>).</summary>
public enum Industry
{
    /// <summary>Accounting.</summary>
    Accounting = 1,

    /// <summary>Agriculture and Non-petrol Natural Resource Extraction.</summary>
    AgricultureAndNonPetrolNaturalResourceExtraction = 2,

    /// <summary>Broadcasting Printing and Publishing.</summary>
    BroadcastingPrintingAndPublishing = 3,

    /// <summary>Brokers.</summary>
    Brokers = 4,

    /// <summary>Building Supply Retail.</summary>
    BuildingSupplyRetail = 5,

    /// <summary>Business Services.</summary>
    BusinessServices = 6,

    /// <summary>Consulting.</summary>
    Consulting = 7,

    /// <summary>Consumer Services.</summary>
    ConsumerServices = 8,

    /// <summary>Design, Direction and Creative Management.</summary>
    DesignDirectionAndCreativeManagement = 9,

    /// <summary>Distributors, Dispatchers and Processors.</summary>
    DistributorsDispatchersAndProcessors = 10,

    /// <summary>Doctor's Offices and Clinics.</summary>
    DoctorsOfficesAndClinics = 11,

    /// <summary>Durable Manufacturing.</summary>
    DurableManufacturing = 12,

    /// <summary>Eating and Drinking Places.</summary>
    EatingAndDrinkingPlaces = 13,

    /// <summary>Entertainment Retail.</summary>
    EntertainmentRetail = 14,

    /// <summary>Equipment Rental and Leasing.</summary>
    EquipmentRentalAndLeasing = 15,

    /// <summary>Financial.</summary>
    Financial = 16,

    /// <summary>Food and Tobacco Processing.</summary>
    FoodAndTobaccoProcessing = 17,

    /// <summary>Inbound Capital Intensive Processing.</summary>
    InboundCapitalIntensiveProcessing = 18,

    /// <summary>Inbound Repair and Services.</summary>
    InboundRepairAndServices = 19,

    /// <summary>Insurance.</summary>
    Insurance = 20,

    /// <summary>Legal Services.</summary>
    LegalServices = 21,

    /// <summary>Non-Durable Merchandise Retail.</summary>
    NonDurableMerchandiseRetail = 22,

    /// <summary>Outbound Consumer Service.</summary>
    OutboundConsumerService = 23,

    /// <summary>Petrochemical Extraction and Distribution.</summary>
    PetrochemicalExtractionAndDistribution = 24,

    /// <summary>Service Retail.</summary>
    ServiceRetail = 25,

    /// <summary>SIG Affiliations.</summary>
    SIGAffiliations = 26,

    /// <summary>Social Services.</summary>
    SocialServices = 27,

    /// <summary>Special Outbound Trade Contractors.</summary>
    SpecialOutboundTradeContractors = 28,

    /// <summary>Specialty Realty.</summary>
    SpecialtyRealty = 29,

    /// <summary>Transportation.</summary>
    Transportation = 30,

    /// <summary>Utility Creation and Distribution.</summary>
    UtilityCreationAndDistribution = 31,

    /// <summary>Vehicle Retail.</summary>
    VehicleRetail = 32,

    /// <summary>Wholesale.</summary>
    Wholesale = 33
}
