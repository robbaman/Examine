﻿using System;
using System.Collections.Generic;
using System.Configuration.Provider;
using System.IO;
using System.Linq;
using System.Web.Configuration;
using System.Web.Hosting;
using System.Xml.Linq;
using Examine.Config;
using Examine.LuceneEngine.Config;
using Examine.LuceneEngine.Providers;
using Examine.Providers;
using Examine.SearchCriteria;
using System.Web;
using Examine.Session;
using Lucene.Net.Index;
using Lucene.Net.Store;

namespace Examine
{
    ///<summary>
    /// Exposes searchers and indexers
    ///</summary>
    public class ExamineManager : ISearcher, IIndexer, IDisposable
    {

        private ExamineManager()
        {
            LoadProviders();
            
            AppDomain.CurrentDomain.DomainUnload += (sender, args) => Dispose();
                
        }

        public static bool InstanceInitialized { get;private set; }

        

        /// <summary>
        /// Singleton
        /// </summary>
        public static ExamineManager Instance
        {
            get
            {
                InstanceInitialized = true;
                return Manager;
            }
        }

        private static readonly ExamineManager Manager = new ExamineManager();

        private readonly object _lock = new object();

        ///<summary>
        /// Returns the default search provider
        ///</summary>
        public BaseSearchProvider DefaultSearchProvider { get; private set; }

        /// <summary>
        /// Returns the collection of searchers
        /// </summary>
        public SearchProviderCollection SearchProviderCollection { get; private set; }

        /// <summary>
        /// Return the colleciton of indexers
        /// </summary>
        public IndexProviderCollection IndexProviderCollection { get; private set; }

        private volatile bool _providersInit = false;

        private void LoadProviders()
        {
            if (!_providersInit)
            {
                lock (_lock)
                {
                    // Do this again to make sure _provider is still null
                    if (!_providersInit)
                    {
                        // Load registered providers and point _provider to the default provider	

                        IndexProviderCollection = new IndexProviderCollection();
                        ProvidersHelper.InstantiateProviders(ExamineSettings.Instance.IndexProviders.Providers, IndexProviderCollection, typeof(BaseIndexProvider));

                        SearchProviderCollection = new SearchProviderCollection();
                        ProvidersHelper.InstantiateProviders(ExamineSettings.Instance.SearchProviders.Providers, SearchProviderCollection, typeof(BaseSearchProvider));

                        //set the default
                        if (!string.IsNullOrEmpty(ExamineSettings.Instance.SearchProviders.DefaultProvider))
                            DefaultSearchProvider =
                                SearchProviderCollection[ExamineSettings.Instance.SearchProviders.DefaultProvider] ??
                                SearchProviderCollection.Cast<BaseSearchProvider>().FirstOrDefault();

                        if (DefaultSearchProvider == null)
                            throw new ProviderException("Unable to load default search provider");

                        _providersInit = true;


                        if (ExamineSettings.Instance.ConfigurationAction != null)
                        {                            
                            ExamineSettings.Instance.ConfigurationAction(this);
                        }

                        //check if we need to rebuild on startup
                        if (ExamineSettings.Instance.RebuildOnAppStart)
                        {
                            foreach (var index in IndexProviderCollection.Cast<IIndexer>())
                            {
                                if (index.IsIndexNew())
                                {
                                    try
                                    {                                        
                                        index.RebuildIndex();                                     

                                    }
                                    catch (Exception ex)
                                    {
                                        var li = index as LuceneIndexer;
                                        try
                                        {
                                            HttpContext.Current.Response.Write("Rebuilding index" +
                                                                               (li != null ? " " + li.Name : "") +
                                                                               " failed");
                                            HttpContext.Current.Response.Write(ex.ToString());
                                        }
                                        catch
                                        {
                                            try
                                            {
                                                File.WriteAllText(HostingEnvironment.MapPath("~/App_Data/ExamineError.txt"), ex.ToString());
                                            }
                                            catch
                                            {
                                                throw;
                                            }
                                        }
                                    }
                                }
                            }    
                        }

                    }
                }
            }
        }


        #region ISearcher Members

        /// <summary>
        /// Uses the default provider specified to search
        /// </summary>
        /// <param name="searchParameters"></param>
        /// <returns></returns>
        /// <remarks>This is just a wrapper for the default provider</remarks>
        public ISearchResults Search(ISearchCriteria searchParameters)
        {
            return DefaultSearchProvider.Search(searchParameters);
        }

        /// <summary>
        /// Uses the default provider specified to search
        /// </summary>
        /// <param name="searchText"></param>
        /// <param name="maxResults"></param>
        /// <param name="useWildcards"></param>
        /// <returns></returns>
        public ISearchResults Search(string searchText, bool useWildcards)
        {
            return DefaultSearchProvider.Search(searchText, useWildcards);
        }


        #endregion

        /// <summary>
        /// Reindex nodes for the providers specified
        /// </summary>
        /// <param name="node"></param>
        /// <param name="category"></param>
        /// <param name="providers"></param>
        public void ReIndexNode(XElement node, string category, IEnumerable<BaseIndexProvider> providers)
        {
            _ReIndexNode(node, category, providers);
        }

        /// <summary>
        /// Deletes index for node for the specified providers
        /// </summary>
        /// <param name="nodeId"></param>
        /// <param name="providers"></param>
        public void DeleteFromIndex(string nodeId, IEnumerable<BaseIndexProvider> providers)
        {
            _DeleteFromIndex(nodeId, providers);
        }

        #region IIndexer Members

        /// <summary>
        /// Reindex nodes for all providers
        /// </summary>
        /// <param name="node"></param>
        /// <param name="category"></param>
        public void ReIndexNode(XElement node, string category)
        {
            _ReIndexNode(node, category, IndexProviderCollection);
        }
        private void _ReIndexNode(XElement node, string type, IEnumerable<BaseIndexProvider> providers)
        {
            foreach (var provider in providers)
            {
                provider.ReIndexNode(node, type);
            }
        }

        /// <summary>
        /// Deletes index for node for all providers
        /// </summary>
        /// <param name="nodeId"></param>
        public void DeleteFromIndex(string nodeId)
        {
            _DeleteFromIndex(nodeId, IndexProviderCollection);
        }    
        private void _DeleteFromIndex(string nodeId, IEnumerable<BaseIndexProvider> providers)
        {
            foreach (var provider in providers)
            {
                provider.DeleteFromIndex(nodeId);
            }
        }

        public void IndexAll(string type)
        {
            _IndexAll(type);
        }
        private void _IndexAll(string type)
        {
            foreach (BaseIndexProvider provider in IndexProviderCollection)
            {
                provider.IndexAll(type);
            }
        }

        public void RebuildIndex()
        {
            _RebuildIndex();
        }
        private void _RebuildIndex()
        {
            foreach (BaseIndexProvider provider in IndexProviderCollection)
            {
                provider.RebuildIndex();
            }
        }

        public IIndexCriteria IndexerData
        {
            get
            {
                throw new NotImplementedException();
            }
            set
            {
                throw new NotImplementedException();
            }
        }

        public bool IndexExists()
        {
            throw new NotImplementedException();
        }

        public bool IsIndexNew()
        {
            throw new NotImplementedException();
        }

        #endregion


        #region ISearcher Members

        /// <summary>
        /// Creates search criteria that defaults to IndexType.Any and BooleanOperation.And
        /// </summary>
        /// <returns></returns>
        public ISearchCriteria CreateSearchCriteria()
        {
            return this.CreateSearchCriteria(string.Empty, BooleanOperation.And);
        }

        public ISearchCriteria CreateSearchCriteria(string type)
        {
            return this.CreateSearchCriteria(type, BooleanOperation.And);
        }

        public ISearchCriteria CreateSearchCriteria(BooleanOperation defaultOperation)
        {
            return this.CreateSearchCriteria(string.Empty, defaultOperation);
        }

        public ISearchCriteria CreateSearchCriteria(string type, BooleanOperation defaultOperation)
        {
            return this.DefaultSearchProvider.CreateSearchCriteria(type, defaultOperation);
        }

        #endregion

        
        /// <summary>
        /// Call this as last thing of the thread or request using Examine.
        /// In web context, this MUST be called add Application_EndRequest. Otherwise horrible memory leaking may occur
        /// </summary>
        public void EndRequest()
        {
            if (ExamineSession.RequireImmediateConsistency)
            {
                ExamineSession.WaitForChanges();
            }

            DisposableCollector.Clean();
        }
        

        /// <summary>
        /// Call this in Application_End.
        /// </summary>
        public void Dispose()
        {
            SearcherContextCollection.Instance.Dispose();
        }
    }
}
