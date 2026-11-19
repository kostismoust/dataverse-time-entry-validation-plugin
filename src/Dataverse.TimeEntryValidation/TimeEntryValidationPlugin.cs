using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.ServiceModel;

namespace Dataverse.TimeEntryValidation
{
    /// <summary>
    /// Prevents project time entries from being saved without a project task or
    /// without a positive cost rate for the resolved resource category.
    ///
    /// Register on msdyn_timeentry Create and Update, PreOperation, Synchronous.
    /// Pass the Cost Price List GUID as the step's unsecure configuration.
    /// </summary>
    public sealed class TimeEntryValidationPlugin : IPlugin
    {
        private const string EntityTimeEntry = "msdyn_timeentry";
        private const string AttributeProject = "msdyn_project";
        private const string AttributeProjectTask = "msdyn_projecttask";
        private const string AttributeResource = "msdyn_bookableresource";
        private const string AttributeResourceCategory = "msdyn_resourcecategory";

        private const string EntityRolePrice = "msdyn_resourcecategorypricelevel";
        private const string RolePriceCategory = "msdyn_resourcecategory";
        private const string RolePricePriceList = "msdyn_pricelist";
        private const string RolePriceAmount = "msdyn_price";

        private readonly Guid _costPriceListId;

        public TimeEntryValidationPlugin(string unsecureConfiguration, string secureConfiguration)
        {
            if (!Guid.TryParse(unsecureConfiguration, out _costPriceListId))
            {
                throw new InvalidPluginExecutionException(
                    "Configure the plugin step with the Cost Price List GUID in unsecure configuration.");
            }
        }

        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var tracing = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            var factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            var service = factory.CreateOrganizationService(context.UserId);

            try
            {
                if (!string.Equals(context.PrimaryEntityName, EntityTimeEntry, StringComparison.OrdinalIgnoreCase))
                    return;

                if (context.Depth > 1)
                {
                    tracing.Trace("Depth {0} > 1; validation skipped.", context.Depth);
                    return;
                }

                if (!context.InputParameters.Contains("Target") ||
                    !(context.InputParameters["Target"] is Entity target))
                    return;

                var isUpdate = string.Equals(context.MessageName, "Update", StringComparison.OrdinalIgnoreCase);
                var project = GetFinalLookup(target, context, isUpdate, AttributeProject, tracing);
                var task = GetFinalLookup(target, context, isUpdate, AttributeProjectTask, tracing);
                var resource = GetFinalLookup(target, context, isUpdate, AttributeResource, tracing);
                var category = GetFinalLookup(target, context, isUpdate, AttributeResourceCategory, tracing);

                // Ignore non-project time entries.
                if (project == null)
                    return;

                if (task == null || task.Id == Guid.Empty)
                {
                    throw new InvalidPluginExecutionException(
                        "A project time entry must be linked to a Project Task.");
                }

                if (category == null && resource != null)
                {
                    category = ResolveDefaultCategoryForResource(service, resource.Id, tracing);
                    if (category != null)
                    {
                        // Persist the resolved role on the Time Entry in PreOperation.
                        target[AttributeResourceCategory] = category;
                    }
                }

                if (category == null)
                {
                    throw new InvalidPluginExecutionException(
                        "No Resource Category could be resolved for the time entry.");
                }

                var rate = GetCostRate(service, category.Id, tracing);
                if (rate == null || rate.Value <= 0m)
                {
                    var categoryName = category.Name ?? category.Id.ToString();
                    throw new InvalidPluginExecutionException(
                        $"Resource Category '{categoryName}' has no positive Cost Rate in the configured Cost Price List.");
                }

                tracing.Trace("Time-entry validation passed. Category={0}, Rate={1}.", category.Id, rate.Value);
            }
            catch (InvalidPluginExecutionException)
            {
                throw;
            }
            catch (FaultException<OrganizationServiceFault> fault)
            {
                tracing.Trace("Dataverse fault: {0}", fault);
                throw new InvalidPluginExecutionException("Time-entry validation failed.", fault);
            }
            catch (Exception ex)
            {
                tracing.Trace("Unhandled error: {0}", ex);
                throw new InvalidPluginExecutionException("Unexpected time-entry validation error.", ex);
            }
        }

        private static EntityReference GetFinalLookup(
            Entity target,
            IPluginExecutionContext context,
            bool isUpdate,
            string attribute,
            ITracingService tracing)
        {
            if (target.Contains(attribute))
            {
                var value = target.GetAttributeValue<EntityReference>(attribute);
                tracing.Trace("{0} from Target: {1}", attribute, value?.Id.ToString() ?? "null");
                return value;
            }

            if (isUpdate && context.PreEntityImages.Contains("PreImage"))
            {
                var preImage = context.PreEntityImages["PreImage"];
                if (preImage != null && preImage.Contains(attribute))
                {
                    var value = preImage.GetAttributeValue<EntityReference>(attribute);
                    tracing.Trace("{0} from PreImage: {1}", attribute, value?.Id.ToString() ?? "null");
                    return value;
                }
            }

            return null;
        }

        private static EntityReference ResolveDefaultCategoryForResource(
            IOrganizationService service,
            Guid resourceId,
            ITracingService tracing)
        {
            var query = new QueryExpression("bookableresourcecategoryassn")
            {
                ColumnSet = new ColumnSet("resourcecategory", "msdyn_isdefault"),
                TopCount = 1
            };
            query.Criteria.AddCondition("resource", ConditionOperator.Equal, resourceId);
            query.AddOrder("msdyn_isdefault", OrderType.Descending);

            var result = service.RetrieveMultiple(query);
            tracing.Trace("Resource-category assignments found: {0}.", result.Entities.Count);
            return result.Entities.Count == 0
                ? null
                : result.Entities[0].GetAttributeValue<EntityReference>("resourcecategory");
        }

        private decimal? GetCostRate(
            IOrganizationService service,
            Guid categoryId,
            ITracingService tracing)
        {
            var query = new QueryExpression(EntityRolePrice)
            {
                ColumnSet = new ColumnSet(RolePriceAmount),
                TopCount = 1
            };
            query.Criteria.AddCondition(RolePriceCategory, ConditionOperator.Equal, categoryId);
            query.Criteria.AddCondition(RolePricePriceList, ConditionOperator.Equal, _costPriceListId);

            var result = service.RetrieveMultiple(query);
            tracing.Trace("Role-price rows found: {0}.", result.Entities.Count);
            return result.Entities.Count == 0
                ? null
                : result.Entities[0].GetAttributeValue<Money>(RolePriceAmount)?.Value;
        }
    }
}
