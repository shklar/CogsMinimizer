﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.Entity.Core.Mapping;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using System.Net;
using CogsMinimizer.Shared;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace OfflineSubscriptionManager
{
    public class Functions
    {
        // This function will be triggered based on the schedule you have set for this WebJob
        [NoAutomaticTrigger]
        public static void ManualTrigger(TextWriter logger)
        {
            Diagnostics.EnsureArgumentNotNull(() => logger);

            ITracer tracer = TracerFactory.CreateTracer(logger);
            tracer.TraceInformation("OfflineSubscriptionManager web job started!");
            tracer.TraceInformation($"Ikey: {ConfigurationManager.AppSettings["env:TelemetryInstrumentationKey"]}");
            tracer.TraceInformation($"Application Key: {ConfigurationManager.AppSettings["ida:ClientID"]}");

            //Top level feature switch to allow easy disabling
            if (ConfigurationManager.AppSettings["env:EnableWebJob"].Equals("True", StringComparison.OrdinalIgnoreCase))
            {
                tracer.TraceInformation("EnableWebJob switch is Enabled.");
                ProcessSubscriptions(tracer);
            }
            else
            {
                tracer.TraceWarning("EnableWebJob switch is disabled. Exiting.");
            }

            tracer.Flush();
        }

        /// <summary>
        /// The main method triggered periodically. Analyzes each one of the registered subscriptions 
        /// and reports to the customer
        /// </summary>
        private static void ProcessSubscriptions(ITracer tracer)
        {
            using (var db = new DataAccess())
            {
                tracer.TraceInformation("DB access created");

                foreach (var sub in db.Subscriptions.ToList())
                {
                    //Analyze the subscription
                    SubscriptionAnalyzer analyzer = new SubscriptionAnalyzer(db, sub, true, tracer);
                    SubscriptionAnalysisResult analysisResult = analyzer.AnalyzeSubscription();
                    sub.LastAnalysisDate = analysisResult.AnalysisStartTime.Date;
                    
                    //Persist analysis results to DB
                    db.SaveChanges();

                    if (ConfigurationManager.AppSettings["env:AllowWebJobEmail"].Equals("True", StringComparison.OrdinalIgnoreCase))
                    {
                        tracer.TraceInformation("AllowWebJobEmail switch is Enabled.");

                        //Send email with the outcome of the analysis
                        EmailSubscriptionAnalysisResult(analysisResult, tracer);
                    }
                    else
                    {
                        tracer.TraceWarning("AllowWebJobEmail switch is disabled. Skipping sending email.");
                    }
                }
            }
        }

        private static void EmailSubscriptionAnalysisResult(SubscriptionAnalysisResult analysisResult, ITracer tracer)
        {
            Subscription sub = analysisResult.AnalyzedSubscription;
            string envDisplayName = ConfigurationManager.AppSettings["env:EnvDisplayName"];
            if (! string.IsNullOrWhiteSpace(envDisplayName))
            {
                envDisplayName = $" [{envDisplayName}]";
            }

            string subject = $"SubMinimizer{envDisplayName}: Subscription Analysis report for {sub.DisplayName}";

            // Don't send mail if all resources are valid if subscription setting set appropriately
            if (sub.SendEmailOnlyInvalidResources)
            {
                if (analysisResult.NotFoundResources.Count == 0 &&
                    analysisResult.NearExpiredResources.Count == 0 &&
                    analysisResult.DeletedResources.Count == 0 && 
                    analysisResult.FailedDeleteResources.Count == 0 &&
                    analysisResult.ExpiredResources.Count == 0)
                {
                    return;
                }
            }

            var message = EmailUtils.CreateEmailMessage(analysisResult, sub);

            var to = new List<Email>();
            var cc = new List<Email>();
            var bcc = new List<Email>();

            //Add To recepients - the subscription admin and anyone with an expired resource 
            to.Add(new Email(sub.ConnectedBy));

            to.AddRange(analysisResult.ExpiredResources.Where(x=> ! string.IsNullOrWhiteSpace(x.Owner)).Select(x=> new Email(x.Owner)));

            //Add CC recepients - the subscription coadmins if so selected by the admin in the settings
            if (sub.SendEmailToCoadmins)
            {
                cc.AddRange(analysisResult.Admins.Select(x=>new Email(x)));         
            }

            //Add BCC recepients - dev team, as configured in the app config
            string devTeam = ConfigurationManager.AppSettings["env:DevTeam"];
            if (devTeam != null)
            {
                bcc.AddRange(devTeam.Split(';').Select(x => new Email(x)));
            }

            var email = new SubMinimizerEmail(subject, message, to, cc, bcc );

            EmailUtils.SendEmail(email, tracer).Wait();
        }

     


    
    }
}
