﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Hosting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OurUmbraco.Community.Controllers;
using OurUmbraco.Community.Meetup.Models;
using OurUmbraco.Community.Models;
using Skybrud.Essentials.Http;
using Skybrud.Social.Meetup.Models.Events;
using Skybrud.Social.Meetup.Models.GraphQl;
using Skybrud.Social.Meetup.Models.Groups;
using Skybrud.Social.Meetup.OAuth;
using Skybrud.Social.Meetup.Responses.Events;
using Umbraco.Core.Cache;
using Umbraco.Core.Logging;
using Umbraco.Web;

namespace OurUmbraco.Community.Meetup
{
    public class MeetupService
    {
        public void UpdateMeetupStats()
        {
            var configPath = HostingEnvironment.MapPath("~/config/MeetupUmbracoGroups.txt");
            // Get the alias (urlname) of each group from the config file
            var aliases = File.ReadAllLines(configPath).Where(x => x.Trim() != "").Distinct().ToArray();

            var counterPath = HostingEnvironment.MapPath("~/App_Data/TEMP/MeetupStatisticsCounter.txt");
            var counter = 0;
            if (File.Exists(counterPath))
            {
                var savedCounter = File.ReadAllLines(counterPath).First();
                int.TryParse(savedCounter, out counter);
            }

            var newCounter = aliases.Length <= counter ? 0 : counter + 1;
            File.WriteAllText(counterPath, newCounter.ToString(), Encoding.UTF8);

            var client = new MeetupOAuth2Client();
            var response = client.DoHttpGetRequest(string.Format("https://api.meetup.com/{0}/events?page=1000&status=past", aliases[counter]));
            var events = MeetupGetEventsResponse.ParseResponse(response).Body;

            var meetupCache = new List<MeetupCacheItem>();
            var meetupCacheFile = HostingEnvironment.MapPath("~/App_Data/TEMP/MeetupStatisticsCache.json");
            if (File.Exists(meetupCacheFile))
            {
                var json = File.ReadAllText(meetupCacheFile);
                using (var stringReader = new StringReader(json))
                using (var jsonTextReader = new JsonTextReader(stringReader))
                {
                    var jsonSerializer = new JsonSerializer();
                    meetupCache = jsonSerializer.Deserialize<List<MeetupCacheItem>>(jsonTextReader);
                }
            }

            foreach (var meetupEvent in events)
            {
                if (meetupCache.Any(x => x.Id == meetupEvent.Id))
                    continue;

                var meetupCacheItem = new MeetupCacheItem
                {
                    Time = meetupEvent.Time,
                    Created = meetupEvent.Created,
                    Description = meetupEvent.Description,
                    HasVenue = meetupEvent.HasVenue,
                    Id = meetupEvent.Id,
                    Link = meetupEvent.Link,
                    Name = meetupEvent.Name,
                    Updated = meetupEvent.Updated,
                    Visibility = meetupEvent.Visibility
                };
                meetupCache.Add(meetupCacheItem);
            }

            var rawJson = JsonConvert.SerializeObject(meetupCache, Formatting.Indented);
            File.WriteAllText(meetupCacheFile, rawJson, Encoding.UTF8);
        }


        public List<MeetupGraphQlGroupResult> GetUpcomingMeetups()
        {
            var groups = new List<MeetupGraphQlGroupResult>();
            
            try
            {
                var configPath = HostingEnvironment.MapPath("~/config/MeetupUmbracoGroups.txt");
                if (File.Exists(configPath) == false)
                {
                    LogHelper.Debug<MeetupsController>("Config file was not found: " + configPath);
                    return null;
                }

                //var groups = new List<MeetupGraphQlGroupResult>();
                
                // Get the alias (urlname) of each group from the config file
                var aliases = File.ReadAllLines(configPath).Where(x => x.Trim() != "").Distinct().ToArray();
                
                groups = UmbracoContext.Current.Application.ApplicationCache.RuntimeCache.GetCacheItem<List<MeetupGraphQlGroupResult>>("UmbracoSearchedMeetups",
                    () =>
                    {
                        var meetupGroups = new List<MeetupGraphQlGroupResult>();
                        // Initialize a new service instance (we don't specify an API key since we're accessing public data) 
                        var service = new Skybrud.Social.Meetup.MeetupService();

                        foreach (var alias in aliases)
                        {
                            try
                            {
                                var query = @"query($urlname: String!) {
  groupByUrlname(urlname: $urlname) {
    id
    name
    logo { id baseUrl preview }
    latitude
    longitude
    description
    urlname
    timezone
    city
    state
    country
    zip
    link
    joinMode
    welcomeBlurb
    upcomingEvents(input: { first: 3 }) {
      count
      pageInfo {
        endCursor
      }
      edges {
        cursor
        node {
          id
          title
          eventUrl
          description
          shortDescription
          howToFindUs
          venue { id name address city state postalCode crossStreet country neighborhood lat lng zoom radius }
          status
          dateTime
          duration
          timezone
          endTime
          createdAt
          eventType
          shortUrl
          isOnline
        }
      }
    }
  }
}";

                                var variables = new JObject {
                                    {"urlname", alias },
                                }.ToString();

                                var request = HttpRequest.Post("/gql", new JObject {
                                    {"query", query},
                                    {"variables", variables}
                                });

                                var response = service.Client.GetResponse(request);

                                // Raw JSON string in response.Body
                                var jObject = JObject.Parse(response.Body);
                                var groupResult = MeetupGraphQlGroupResult.Parse(jObject);
                                if(groupResult != null)
                                    meetupGroups.Add(groupResult);
                            }
                            catch (Exception ex)
                            {
                                LogHelper.Error<MeetupsController>("Could not get events from meetup.com for group with alias: " + alias, ex);
                            }
                        }

                        return meetupGroups;
                    }, TimeSpan.FromMinutes(30));
            }
            catch (Exception ex)
            {
                LogHelper.Error<MeetupsController>("Could not get events from meetup.com", ex);
            }

            return groups;
        }
    }
    
    // Root myDeserializedClass = JsonConvert.DeserializeObject<Root>(myJsonResponse);
    public class Logo
    {
        public string id { get; set; }
        public string baseUrl { get; set; }
    }

    public class PageInfo
    {
        public string endCursor { get; set; }
    }

    public class Venue
    {
        public string id { get; set; }
        public string name { get; set; }
        public string address { get; set; }
        public string city { get; set; }
        public string state { get; set; }
        public string postalCode { get; set; }
        public object crossStreet { get; set; }
        public string country { get; set; }
        public object neighborhood { get; set; }
        public double lat { get; set; }
        public double lng { get; set; }
        public int zoom { get; set; }
        public int radius { get; set; }
    }

    public class Node
    {
        public string id { get; set; }
        public string title { get; set; }
        public string eventUrl { get; set; }
        public string description { get; set; }
        public string shortDescription { get; set; }
        public string howToFindUs { get; set; }
        public Venue venue { get; set; }
        public string status { get; set; }
        public string dateTime { get; set; }
        public string duration { get; set; }
        public string timezone { get; set; }
        public string endTime { get; set; }
        public object createdAt { get; set; }
        public string eventType { get; set; }
        public string shortUrl { get; set; }
        public bool isOnline { get; set; }
    }

    public class Edge
    {
        public string cursor { get; set; }
        public Node node { get; set; }
    }

    public class UpcomingEvents
    {
        public int count { get; set; }
        public PageInfo pageInfo { get; set; }
        public List<Edge> edges { get; set; }
    }

    public class GroupByUrlname
    {
        public string id { get; set; }
        public string name { get; set; }
        public Logo logo { get; set; }
        public double latitude { get; set; }
        public double longitude { get; set; }
        public double lat { get; set; }
        public double lon { get; set; }
        public string description { get; set; }
        public string urlname { get; set; }
        public string timezone { get; set; }
        public string city { get; set; }
        public string state { get; set; }
        public string country { get; set; }
        public string zip { get; set; }
        public string link { get; set; }
        public string joinMode { get; set; }
        public object welcomeBlurb { get; set; }
        public UpcomingEvents upcomingEvents { get; set; }
    }

    public class Data
    {
        public GroupByUrlname groupByUrlname { get; set; }
    }

    public class Root
    {
        public Data data { get; set; }
    }


}