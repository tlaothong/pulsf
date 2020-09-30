using System.Threading.Tasks;
using Pulumi;
using Pulumi.AzureNextGen.Resources.Latest;
using Pulumi.AzureNextGen.Storage.Latest;
using Pulumi.AzureNextGen.Storage.Latest.Inputs;
using Pulumi.AzureNextGen.Network.Latest;
using nwin = Pulumi.AzureNextGen.Network.Latest.Inputs;


class MyStack : Stack
{
    public MyStack()
    {
        var tags = new InputMap<string> {
            { "resourceType", "Service Fabric" },
            { "clusterName", "thepulsf" }
        };

        // Create an Azure Resource Group
        var resourceGroup = new ResourceGroup("resourceGroup", new ResourceGroupArgs
        {
            ResourceGroupName = "apulsf",
            Location = "SoutheastAsia",
            Tags = tags,
        });

        // Create a Storage Account for Log
        var storageAccountLog = new StorageAccount("saLog", new StorageAccountArgs
        {
            ResourceGroupName = resourceGroup.Name,
            AccountName = "sflogstore",
            Location = resourceGroup.Location,
            Sku = new SkuArgs
            {
                Name = "Standard_LRS",
                Tier = "Standard"
            },
            Kind = "StorageV2",
            Tags = tags,
        });
        // Create a Storage Account for Application Diagnostics
        var storageAccountAppDx = new StorageAccount("saAppDx", new StorageAccountArgs
        {
            ResourceGroupName = resourceGroup.Name,
            AccountName = "sfappdxstore",
            Location = resourceGroup.Location,
            Sku = new SkuArgs
            {
                Name = "Standard_LRS",
                Tier = "Standard"
            },
            Kind = "StorageV2",
            Tags = tags,
        });
        // Virtual Network
        var vnet = new VirtualNetwork("vnet", new VirtualNetworkArgs {
            ResourceGroupName = resourceGroup.Name,
            VirtualNetworkName = "thevnet4sf",
            Location = resourceGroup.Location,
            AddressSpace = new nwin.AddressSpaceArgs {
                AddressPrefixes = "10.10.0.0/16",
            },
            Subnets = {
                new nwin.SubnetArgs {
                    Name = "Subnet0",
                    AddressPrefix = "10.10.10.0/24",
                }
            },
            Tags = tags,
        });
        // Public IP Address
        var pubIpAddr = new PublicIPAddress("pubip4sf", new PublicIPAddressArgs {
            ResourceGroupName = resourceGroup.Name,
            PublicIpAddressName = "thesfpubip",
            Location = resourceGroup.Location,
            DnsSettings = new nwin.PublicIPAddressDnsSettingsArgs {
                DomainNameLabel = "thepulsf",
            },
            PublicIPAllocationMethod = "Dynamic",
            Tags = tags,
        });

        // Load Balancer
        var backe1 = new LoadBalancerBackendAddressPool("lbbeaddrpool", new LoadBalancerBackendAddressPoolArgs{
            Name = "lbbeaddrpool",
            ResourceGroupName = resourceGroup.Name,
            BackendAddressPoolName = "lbbeaddrpool",
            LoadBalancerName = "thepulsflb4set0",
        });
        var loadBalancer = new LoadBalancer("lb", new LoadBalancerArgs {
            ResourceGroupName = resourceGroup.Name,
            Location = resourceGroup.Location,
            LoadBalancerName = "thepulsflb4set0",
            FrontendIPConfigurations = new nwin.FrontendIPConfigurationArgs {
                Name = "lbipconfig",
                PublicIPAddress = new nwin.PublicIPAddressArgs {
                    Id = pubIpAddr.Id,
                },
            },
            LoadBalancingRules = {
                new nwin.LoadBalancingRuleArgs {
                    Name = "LBRule",
                    FrontendIPConfiguration = new nwin.SubResourceArgs {
                        Id = "lbipconfig",
                    },
                    FrontendPort = 19000,
                    BackendAddressPool = new nwin.SubResourceArgs {
                        Id = backe1.Id,
                    },
                    BackendPort = 19080,
                    IdleTimeoutInMinutes = 5,
                    Protocol = "tcp",
                    EnableFloatingIP = false,
                    Probe = new nwin.SubResourceArgs {
                        Id = "lbsfgwprobe",
                    },
                }
            },
            Probes = {
                new nwin.ProbeArgs {
                    Name = "lbsfgwprobe",
                    IntervalInSeconds = 5,
                    NumberOfProbes = 2,
                    Port = 19000,
                    Protocol = "tcp",
                }
            },
            Tags = tags,
        });

        // Export the primary key of the Storage Account
        this.PrimaryStorageKey = Output.Tuple(resourceGroup.Name, storageAccountAppDx.Name).Apply(names =>
            Output.CreateSecret(GetStorageAccountPrimaryKey(names.Item1, names.Item2)));
    }

    [Output]
    public Output<string> PrimaryStorageKey { get; set; }

    private static async Task<string> GetStorageAccountPrimaryKey(string resourceGroupName, string accountName)
    {
        var accountKeys = await ListStorageAccountKeys.InvokeAsync(new ListStorageAccountKeysArgs
        {
            ResourceGroupName = resourceGroupName,
            AccountName = accountName
        });
        return accountKeys.Keys[0].Value;
    }
}
