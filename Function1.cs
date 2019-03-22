using System;
using System.Linq;
using System.Net;
using System.Configuration;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.Documents;
using Newtonsoft.Json.Linq;

namespace SampleFunction
{
    public static class CosmosScaling
    {

        public static string GetEnvironmentVariable(string name)
        {
            return System.Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process);
        }


        [FunctionName("CosmosScaling")]
        public static async Task<HttpResponseMessage> Run([HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)]HttpRequestMessage req, TraceWriter log)
        {
            try
            {
                //1) initialize the document client
                using (DocumentClient client = new DocumentClient(new Uri("https://jabcos.documents.azure.com:443/"), "0xS940aFzWH6dSwBzURaZ3iXe4BYg8HNmAwhnB49GJpJDXbCCHOTJcJCwyOzvYjJf3fRuh4dLMTqYy9wjZhCxg=="))
                {
                    Console.Write(client.ServiceEndpoint);
                    //2) get the database self link
                    string selfLink = client.CreateDocumentCollectionQuery(
                                        UriFactory.CreateDatabaseUri(GetEnvironmentVariable("CosmosDB_DatabaseId")))
                                            .Where(c => c.Id == GetEnvironmentVariable("CosmosDB_CollectionId"))
                                            .AsEnumerable()
                                            .FirstOrDefault()
                                            .SelfLink;

                    //3) get the current offer for the collection
                    Offer offer = client.CreateOfferQuery()
                                    .Where(r => r.ResourceLink == selfLink)
                                    .AsEnumerable()
                                    .SingleOrDefault();

                    //4) get the current throughput from the offer
                    int throughputCurrent = (int)offer.GetPropertyValue<JObject>("content").GetValue("offerThroughput");
                    log.Info(string.Format("Current provisioned throughput: {0} RU", throughputCurrent.ToString()));

                    //5) get the RU increment from AppSettings and parse to an int
                    if (int.TryParse(GetEnvironmentVariable("CosmosDB_RUIncrement"), out int RUIncrement))
                    {
                        //5.a) create the new offer with the throughput increment added to the current throughput
                        int newThroughput = throughputCurrent + RUIncrement;
                        offer = new OfferV2(offer, newThroughput);

                        //5.b) persist the changes
                        await client.ReplaceOfferAsync(offer);
                        log.Info(string.Format("New provisioned througput: {0} RU", newThroughput.ToString()));
                        return req.CreateResponse(HttpStatusCode.OK, "The collection's throughput was changed...");
                    }
                    else
                    {
                        //5.c) if the throughputIncrement cannot be parsed return throughput not changed
                        return req.CreateResponse(HttpStatusCode.OK, "PARSE ERROR: The collection's throughput was not changed...");
                    }
                }
            }
            catch (Exception e)
            {
                log.Info(e.Message + e.InnerException + e.StackTrace);
                return req.CreateResponse(HttpStatusCode.OK, e.Message + e.InnerException + e.StackTrace);
            }
        }
    }
}
