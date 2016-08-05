﻿using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.Portal;
using Esri.ArcGISRuntime.Security;
using Esri.ArcGISRuntime.UI;
using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace IntegratedWindowsAuth
{
    public partial class MainWindow : Window
    {
        //TODO - Add the URL for your IWA-secured portal
        const string SecuredPortalUrl = "https://portaliwaqa.ags.esri.com/gis/sharing";

        //TODO - Add the URL for a portal containing public content (your ArcGIS Online Organization, e.g.)
        const string PublicPortalUrl = "http://www.arcgis.com/sharing/rest"; 

        //TODO [optional] - Add hard-coded account information (if present, a network credential will be created on app initialize)
        // Note: adding bogus credential info can provide a way to verify unauthorized users will be challenged for a log in
        const string NetworkUsername = "";
        const string NetworkPassword = "";
        const string NetworkDomain = "";

        // Variables to point to public and secured portals
        ArcGISPortal _iwaSecuredPortal = null;
        ArcGISPortal _publicPortal = null;

        // Flag variable to track if the user is looking at maps from the public or secured portal
        bool _usingPublicPortal;

        // Flag to track if the user has canceled the login dialog
        bool _canceledLogin;

        public MainWindow()
        {
            InitializeComponent();

            // Call a function to set up the AuthenticationManager and add a hard-coded credential (if defined)
            Initialize();
        }

        private void Initialize()
        {
            // Define a challenge handler method for the AuthenticationManager 
            // (this method handles getting credentials when a secured resource is encountered)
            AuthenticationManager.Current.ChallengeHandler = new ChallengeHandler(CreateCredentialAsync);

            // Note: for IWA-secured services, your current system credentials will be used by default and you will only
            //       be challenged for resources to which your system account doesn't have access

            // Check for hard-coded username, password, and domain values
            if (!string.IsNullOrEmpty(NetworkUsername) &&
                !string.IsNullOrEmpty(NetworkPassword) &&
                !string.IsNullOrEmpty(NetworkDomain))
            {
                // Add a hard-coded network credential (other than the one that started the app, in other words)
                ArcGISNetworkCredential hardcodedCredential = new ArcGISNetworkCredential
                {
                    Credentials = new System.Net.NetworkCredential(NetworkUsername, NetworkPassword, NetworkDomain),
                    ServiceUri = new Uri(SecuredPortalUrl)
                };
                AuthenticationManager.Current.AddCredential(hardcodedCredential);
            }
        }
        
        // Prompt the user for a credential if unauthorized access to a secured resource is attempted
        public async Task<Credential> CreateCredentialAsync(CredentialRequestInfo info)
        {
            Credential credential = null;
            try
            {
                // Dispatch to the UI thread to show the login UI
                credential = this.Dispatcher.Invoke(new Func<Credential>(() =>
                {
                    Credential cred = null;

                    // Exit if the user clicked "Cancel" in the login window
                    // (if the user can't provide credentials for a resource they will continue to be challenged)
                    if (_canceledLogin)
                    {
                        _canceledLogin = false;
                        return null;
                    }

                    // Create a new login window
                    var win = new LoginWindow();
                    win.Owner = this;

                    // Show the window to get user input (if canceled, false is returned)
                    _canceledLogin = (win.ShowDialog() == false);

                    if (!_canceledLogin)
                    {
                        // Get the credential information provided
                        var username = win.UsernameTextBox.Text;
                        var password = win.PasswordTextBox.Password;
                        var domain = win.DomainTextBox.Text;

                        // Create a new network credential using the user input and the URI of the resource
                        cred = new ArcGISNetworkCredential()
                        {
                            Credentials = new System.Net.NetworkCredential(username, password, domain),
                            ServiceUri = info.ServiceUri
                        };
                    }

                    // Return the credential
                    return cred;
                }));
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Exception: " + ex.Message);
            }

            // Add the credential to the AuthenticationManager
            AuthenticationManager.Current.AddCredential(credential);

            // Return the credential
            return credential;
        }
        
        // Search the public portal for web maps and display the results in a list box.
        private async void SearchPublicMapsClick(object sender, RoutedEventArgs e)
        {
            // Set the flag variable to indicate this is the public portal
            // (if the user wants to load a map, will need to know which portal it came from)
            _usingPublicPortal = true;

            // Clear any current items from the list 
            MapItemListBox.Items.Clear();

            // Show status message and the status bar
            MessagesTextBlock.Text = "Searching for web map items on the public portal.";
            ProgressStatus.Visibility = Visibility.Visible;

            // Store information about the portal connection
            var connectionInfo = new StringBuilder();

            try
            {
                // Create an instance of the public portal
                _publicPortal = await ArcGISPortal.CreateAsync(new Uri(PublicPortalUrl));

                // Report a successful connection
                connectionInfo.AppendLine("Connected to the portal on " + _publicPortal.Uri.Host);
                connectionInfo.AppendLine("Version: " + _publicPortal.CurrentVersion);

                // Report the username used for this connection
                if (_publicPortal.CurrentUser != null)
                {
                    connectionInfo.AppendLine("Connected as: " + _publicPortal.CurrentUser.UserName);
                }
                else
                {
                    connectionInfo.AppendLine("Anonymous"); 
                }
          
                // Search the public portal for web maps
                // (exclude the term "web mapping application" since it also contains the string "web map")
                var items = await _publicPortal.SearchItemsAsync(new SearchParameters("type:(\"web map\" NOT \"web mapping application\")"));

                // Build a list of items from the results that shows the map title and stores the item ID (with the Tag property)
                var resultItems = from r in items.Results select new ListBoxItem { Tag = r.Id, Content = r.Title };

                // Add the list items
                foreach (var itm in resultItems)
                {
                    MapItemListBox.Items.Add(itm);
                }
            }
            catch (Exception ex)
            {
                // Report errors connecting to or searching the public portal
                connectionInfo.AppendLine(ex.Message);
            }
            finally
            {
                // Show messages, hide progress bar
                MessagesTextBlock.Text = connectionInfo.ToString();
                ProgressStatus.Visibility = Visibility.Hidden;
            }
        }

        // Search the IWA-secured portal for web maps and display the results in a list box.        
        private async void SearchSecureMapsButton_Click(object sender, RoutedEventArgs e)
        {
            // Set the flag variable to indicate this is the secure portal
            // (if the user wants to load a map, will need to know which portal it came from)
            _usingPublicPortal = false;

            // Clear any current items in the list
            MapItemListBox.Items.Clear();

            // Show status message and the status bar
            MessagesTextBlock.Text = "Searching for web map items on the secure portal.";
            ProgressStatus.Visibility = Visibility.Visible;

            // Store connection information to report 
            var connectionInfo = new StringBuilder();

            try
            {
                // Create an instance of the IWA-secured portal
                _iwaSecuredPortal = await ArcGISPortal.CreateAsync(new Uri(SecuredPortalUrl));

                // Report a successful connection
                connectionInfo.AppendLine("Connected to the portal on " + _iwaSecuredPortal.Uri.Host);
                connectionInfo.AppendLine("Version: " + _iwaSecuredPortal.CurrentVersion);

                // Report the username used for this connection
                if (_iwaSecuredPortal.CurrentUser != null)
                {
                    connectionInfo.AppendLine("Connected as: " + _iwaSecuredPortal.CurrentUser.UserName);
                }
                else
                {
                    // This shouldn't happen, need to authentication to connect
                    connectionInfo.AppendLine("Anonymous?!"); 
                }

                // Search the secured portal for web maps
                // (exclude the term "web mapping application" since it also contains the string "web map")
                var items = await _iwaSecuredPortal.SearchItemsAsync(new SearchParameters("type:(\"web map\" NOT \"web mapping application\")"));

                // Build a list of items from the results that shows the map title and stores the item ID (with the Tag property)
                var resultItems = from r in items.Results select new ListBoxItem { Tag = r.Id, Content = r.Title };

                // Add the list items
                foreach (var itm in resultItems)
                {
                    MapItemListBox.Items.Add(itm);
                }
            }
            catch (Exception ex)
            {
                // Report errors connecting to or searching the secured portal
                connectionInfo.AppendLine(ex.Message);
            }
            finally
            {
                // Show messages, hide progress bar
                MessagesTextBlock.Text = connectionInfo.ToString();
                ProgressStatus.Visibility = Visibility.Hidden;
            }
        }

        private async void AddMapItemClick(object sender, RoutedEventArgs e)
        {
            // Get a web map from the selected portal item and display it in the app
            if (this.MapItemListBox.SelectedItem == null) { return; }

            // Clear status messages
            MessagesTextBlock.Text = string.Empty;

            // Store status (or errors) when adding the map
            var statusInfo = new StringBuilder();

            try
            {
                // Clear the current MapView control from the app
                MyMapGrid.Children.Clear();

                // See if using the public or secured portal; get the appropriate object reference
                ArcGISPortal portal = null;
                if (_usingPublicPortal)
                {
                    portal = _publicPortal;
                }
                else
                {
                    portal = _iwaSecuredPortal;
                }

                // Throw an exception if the portal is null
                if (portal == null)
                {
                    throw new Exception("Portal has not been instantiated.");
                }

                // Get the portal item ID from the selected list box item (read it from the Tag property)
                var itemId = (this.MapItemListBox.SelectedItem as ListBoxItem).Tag.ToString();

                // Use the item ID to create an ArcGISPortalItem from the appropriate portal 
                var portalItem = await ArcGISPortalItem.CreateAsync(portal, itemId);
                
                if (portalItem != null)
                {
                    // Create a Map using the web map (portal item)
                    Map webMap = new Map(portalItem);

                    // Create a new MapView control to display the Map
                    MapView myMapView = new MapView();
                    myMapView.Map = webMap;

                    // Add the MapView to the app
                    MyMapGrid.Children.Add(myMapView);
                }

                // Report success
                statusInfo.AppendLine("Successfully loaded web map from item #" + itemId + " from " + portal.Uri.Host);
            }
            catch (Exception ex)
            {
                // Add an error message
                statusInfo.AppendLine("Error accessing web map: " + ex.Message);
            }
            finally
            {
                // Show messages
                MessagesTextBlock.Text = statusInfo.ToString();
            }
        }

    }
}
