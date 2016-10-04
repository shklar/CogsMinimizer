﻿using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace CogsMinimizer.Shared
{
    /// <summary>
    /// Represents a subscription that can be managed
    /// </summary>
    public class Subscription
    {
        public const int DEFAULT_EXPIRATION_INTERVAL_IN_DAYS = 7;

        public string Id { get; set; }
       
        public string DisplayName { get; set; }
        public string OrganizationId { get; set; }
        [NotMapped]
        public bool IsConnected { get; set; }
       
        public DateTime ConnectedOn { get; set; }
     
        public string ConnectedBy { get; set; }
        [NotMapped]
        public bool AzureAccessNeedsToBeRepaired { get; set; }

        public int ExpirationIntervalInDays  { get; set; }

        public DateTime LastAnalysisDate { get; set; }
    }
}