using System.Threading.Tasks;
using Pulumi;
using Pulumi.AzureNextGen.Resources.Latest;
using Pulumi.AzureNextGen.Storage.Latest;
using Pulumi.AzureNextGen.Storage.Latest.Inputs;
using Pulumi.AzureNextGen.Network.Latest;
using nwin = Pulumi.AzureNextGen.Network.Latest.Inputs;
using lb = Pulumi.Azure.Lb;
using Pulumi.AzureNextGen.Compute.Latest;
using cpi = Pulumi.AzureNextGen.Compute.Latest.Inputs;
using Pulumi.AzureNextGen.ServiceFabric.Latest;
using sfi = Pulumi.AzureNextGen.ServiceFabric.Latest.Inputs;

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
            Tags = tags,
        });
        var bepool0 = new lb.BackendAddressPool("bepool0", new lb.BackendAddressPoolArgs {
            Name = "bepool0",
            ResourceGroupName = resourceGroup.Name,
            LoadbalancerId = loadBalancer.Id,
        });
        var lbPorts = new [] { 19000, 19080, 80, 443 };
        for (int i = 0; i < lbPorts.Length; i++)
        {
            var port = lbPorts[i];

            var probe = new lb.Probe($"lbProbe{i}", new lb.ProbeArgs {
                ResourceGroupName = resourceGroup.Name,
                LoadbalancerId = loadBalancer.Id,
                Port = port,
                IntervalInSeconds = 5,
                NumberOfProbes = 2,
                Protocol = "tcp",
            });
            var rule = new lb.Rule($"lbRule{i}", new lb.RuleArgs {
                ResourceGroupName = resourceGroup.Name,
                LoadbalancerId = loadBalancer.Id,
                FrontendIpConfigurationName = "lbipconfig",
                FrontendPort = port,
                BackendAddressPoolId = bepool0.Id,
                BackendPort = port,
                IdleTimeoutInMinutes = 5,
                EnableFloatingIp = false,
                Protocol = "tcp",
            });
        }
        var lbNatPool = new lb.NatPool("lbNatPool", new lb.NatPoolArgs {
            ResourceGroupName = resourceGroup.Name,
            LoadbalancerId = loadBalancer.Id,
            FrontendIpConfigurationName = "lbipconfig",
            FrontendPortStart = 3389,
            FrontendPortEnd = 4500,
            BackendPort = 3389,
            Protocol = "tcp",
        });

        var sfCluster = new Cluster("thesf", new ClusterArgs{
            ClusterName = "thesf",
            ResourceGroupName = resourceGroup.Name,
            Location = resourceGroup.Location,

            AddOnFeatures = {
                "DnsService",
                "BackupRestoreService"
            },
            Certificate = new sfi.CertificateDescriptionArgs {
                Thumbprint = "4426C164D9E66C2C813DEEC0486F235C0E933212",
                X509StoreName = "My",
            },
            DiagnosticsStorageAccountConfig = new sfi.DiagnosticsStorageAccountConfigArgs {
                BlobEndpoint = storageAccountLog.PrimaryEndpoints.Apply(it => it.Blob),
                ProtectedAccountKeyName = "StorageAccountKey1",
                QueueEndpoint = storageAccountLog.PrimaryEndpoints.Apply(it => it.Queue),
                StorageAccountName = storageAccountLog.Name,
                TableEndpoint = storageAccountLog.PrimaryEndpoints.Apply(it => it.Table),
            },
            FabricSettings = {
                new sfi.SettingsSectionDescriptionArgs {
                    Parameters = {
                        new sfi.SettingsParameterDescriptionArgs {
                            Name = "SecretEncryptionCertThumbprint",
                            Value = "4426C164D9E66C2C813DEEC0486F235C0E933212",
                        },
                        new sfi.SettingsParameterDescriptionArgs {
                            Name = "SecretEncryptionCertX509StoreName",
                            Value = "My",
                        },
                    },
                    Name = "BackupRestoreService",
                },
                new sfi.SettingsSectionDescriptionArgs {
                    Parameters = {
                        new sfi.SettingsParameterDescriptionArgs {
                            Name = "ClusterProtectionLevel",
                            Value = "EncryptAndSign",
                        },
                    },
                    Name = "Security",
                },
            },
            ManagementEndpoint = Output.Format($"https://{pubIpAddr.DnsSettings.Apply(it => it?.Fqdn)}:19080"),
            NodeTypes = {
                new sfi.NodeTypeDescriptionArgs {
                    Name = "Type925",
                    ApplicationPorts = new sfi.EndpointRangeDescriptionArgs {
                        StartPort = 20000,
                        EndPort = 30000,
                    },
                    ClientConnectionEndpointPort = 19000,
                    DurabilityLevel = "Silver",
                    EphemeralPorts = new sfi.EndpointRangeDescriptionArgs {
                        StartPort = 49152,
                        EndPort = 65534,
                    },
                    HttpGatewayEndpointPort = 19080,
                    IsPrimary = true,
                    VmInstanceCount = 5,
                },
            },
            ReliabilityLevel = "Silver",
            UpgradeMode = "Automatic",
            VmImage = "Windows",
        });


        Key1 = GetStorageKey(resourceGroup, storageAccountLog, 0);
        Key2 = GetStorageKey(resourceGroup, storageAccountLog, 1);
        Input<string> key1 = Key1;
        Input<string> key2 = Key2;

        var vmScaleSet = new VirtualMachineScaleSet("sfScaleSet", new VirtualMachineScaleSetArgs {
            ResourceGroupName = resourceGroup.Name,
            Location = resourceGroup.Location,
            VmScaleSetName = "sfScaleSet0",
            Sku = new cpi.SkuArgs {
                Name = "Standard_D2s_v3",
                Capacity = 5,
                Tier = "Standard",
            },
            Overprovision = false,
            UpgradePolicy = new cpi.UpgradePolicyArgs {
                Mode = "Automatic",
            },
            VirtualMachineProfile = new cpi.VirtualMachineScaleSetVMProfileArgs {
                ExtensionProfile = new cpi.VirtualMachineScaleSetExtensionProfileArgs {
                    Extensions = {
                        new cpi.VirtualMachineScaleSetExtensionArgs {
                            Name = "Type925_ServiceFabricNode",
                            Type = "ServiceFabricNode",
                            TypeHandlerVersion = "1.1",
                            AutoUpgradeMinorVersion = true,
                            // EnableAutomaticUpgrade = true,   // MUST NOT ENABLED
                            ProtectedSettings = {
                                { "StorageAccountKey1", key1 },
                                { "StorageAccountKey2", key2 },
                            },
                            Publisher = "Microsoft.Azure.ServiceFabric",
                            Settings = {
                                // { "id", "thepulsf" },
                                // { "clusterId", "thepulsf" },
                                // { "clusterEndpoint", pubIpAddr.DnsSettings.Apply(it => it?.Fqdn) },
                                { "clusterEndpoint", sfCluster.ClusterEndpoint },
                                { "nodeTypeRef", "Type925" },
                                { "dataPath", @"D:\\SvcFab" },
                                { "durabilityLevel", "Silver" },
                                { "enableParallelJobs", true },
                                { "nicPrefixOverride", "10.10.10.0/24" },
                                { "certificate", new InputMap<string>
                                    {
                                        { "thumbprint", "4426C164D9E66C2C813DEEC0486F235C0E933212" },
                                        { "x509StoreName", "My" },
                                    } 
                                },
                            },
                        },
                        new cpi.VirtualMachineScaleSetExtensionArgs {
                            Name = "VMDiagnosticsVmExt_Set0",
                            Type = "IaaSDiagnostics",
                            TypeHandlerVersion = "1.5",
                            ProtectedSettings = {
                                { "storageAccountName", storageAccountAppDx.Name },
                                { "storageAccountKey", GetStorageKey(resourceGroup, storageAccountAppDx, 0) },
                                { "storageAccountEndPoint", "https://core.windows.net/" },
                            },
                            Publisher = "Microsoft.Azure.Diagnostics",
                            Settings = {
                                { "WadCfg", new InputMap<object>
                                    {
                                        { "DiagnosticMonitorConfiguration", new InputMap<object> 
                                            {
                                                { "overallQuotaInMB", "50000" },
                                                { "EtwProviders", new InputMap<object>
                                                    {
                                                        { "EtwEventSourceProviderConfiguration", new InputList<InputMap<object>>
                                                            {
                                                                new InputMap<object> 
                                                                {
                                                                    { "provider", "Microsoft-ServiceFabric-Actors" },
                                                                    { "scheduledTransferKeywordFilter", "1" },
                                                                    { "scheduledTransferPeriod", "PT5M" },
                                                                    { "DefaultEvents", new InputMap<string> 
                                                                        { { "eventDestination", "ServiceFabricReliableActorEventTable" } } 
                                                                    },
                                                                },
                                                                new InputMap<object> 
                                                                {
                                                                    { "provider", "Microsoft-ServiceFabric-Services" },
                                                                    { "scheduledTransferPeriod", "PT5M" },
                                                                    { "DefaultEvents", new InputMap<string> 
                                                                        { { "eventDestination", "ServiceFabricReliableServiceEventTable" } } 
                                                                    },
                                                                },
                                                            } 
                                                        },
                                                        { "EtwManifestProviderConfiguration", new InputList<InputMap<object>>
                                                            {   new InputMap<object> {
                                                                { "provider", "cbd93bc2-71e5-4566-b3a7-595d8eeca6e8" },
                                                                { "scheduledTransferLogLevelFilter", "Information" },
                                                                { "scheduledTransferKeywordFilter", "4611686018427387904" },
                                                                { "scheduledTransferPeriod", "PT5M" },
                                                                { "DefaultEvents", new InputMap<string> 
                                                                    { { "eventDestination", "ServiceFabricSystemEventTable" } } 
                                                                },
                                                            } }
                                                        },
                                                    }
                                                },
                                            }
                                        },
                                    }
                                },
                                { "StorageAccount", storageAccountAppDx.Name },
                            },
                        },
                    },
                },
                NetworkProfile = new cpi.VirtualMachineScaleSetNetworkProfileArgs {
                    NetworkInterfaceConfigurations = {
                        new cpi.VirtualMachineScaleSetNetworkConfigurationArgs {
                            Name = "nicSfSet-0",
                            IpConfigurations = {
                                new cpi.VirtualMachineScaleSetIPConfigurationArgs {
                                    Name = "nicSfSet-0",
                                    LoadBalancerBackendAddressPools = new cpi.SubResourceArgs { Id = bepool0.Id },
                                    LoadBalancerInboundNatPools = new cpi.SubResourceArgs { Id = lbNatPool.Id },
                                    Subnet = new cpi.ApiEntityReferenceArgs {
                                        Id = vnet.Subnets.First().Apply(it => it.Id ?? string.Empty),
                                    },
                                },
                            },
                            Primary = true,
                        },
                    },
                },
                OsProfile = new cpi.VirtualMachineScaleSetOSProfileArgs {
                    AdminUsername = "sfvmadmin",
                    AdminPassword = "P@ssw0rd9",
                    ComputerNamePrefix = "Type925",
                    Secrets = {
                        new cpi.VaultSecretGroupArgs {
                            SourceVault = new cpi.SubResourceArgs { 
                                Id = "/subscriptions/5fb1076d-8ce2-4bf8-90af-d53f1b8f8289/resourceGroups/prepkvault/providers/Microsoft.KeyVault/vaults/thekv4sf" 
                            },
                            VaultCertificates = {
                                new cpi.VaultCertificateArgs {
                                    CertificateStore = "My",
                                    CertificateUrl = "https://thekv4sf.vault.azure.net/secrets/thesfcert/4379416a51dc4570bda6bc79a6fbfa59",
                                },
                            },
                        },
                    },
                },
                StorageProfile = new cpi.VirtualMachineScaleSetStorageProfileArgs {
                    ImageReference = new cpi.ImageReferenceArgs {
                        Publisher = "MicrosoftWindowsServer",
                        Offer = "WindowsServer",
                        Sku = "2019-Datacenter-with-Containers",
                        Version = "latest",
                    },
                    OsDisk = new cpi.VirtualMachineScaleSetOSDiskArgs {
                        Caching = "ReadOnly",
                        CreateOption = "FromImage",
                        ManagedDisk = new cpi.VirtualMachineScaleSetManagedDiskParametersArgs {
                            StorageAccountType = "Standard_LRS",
                        },
                    },
                },
            },
        });
        
        // Export the primary key of> the Storage Account
        this.PrimaryStorageKey = Output.Tuple(resourceGroup.Name, storageAccountAppDx.Name).Apply(names =>
            Output.CreateSecret(GetStorageAccountKey(names.Item1, names.Item2, 0)));
    }

    [Output]
    public Output<string> PrimaryStorageKey { get; set; }
    [Output]
    public Output<string> Key1 {get;set;}
    [Output]
    public Output<string> Key2 {get;set;}

    private static async Task<string> GetStorageAccountPrimaryKey(string resourceGroupName, string accountName)
    {
        var accountKeys = await ListStorageAccountKeys.InvokeAsync(new ListStorageAccountKeysArgs
        {
            ResourceGroupName = resourceGroupName,
            AccountName = accountName
        });
        return accountKeys.Keys[0].Value;
    }

    private static Output<string> GetStorageKey(ResourceGroup group, StorageAccount acc, int keyIndex) {
        return Output.Tuple(group.Name, acc.Name).Apply(names =>
            Output.CreateSecret(GetStorageAccountKey(names.Item1, names.Item2, keyIndex)));
    }

    private static async Task<string> GetStorageAccountKey(string resourceGroupName, string accountName, int keyIndex)
    {
        var accountKeys = await ListStorageAccountKeys.InvokeAsync(new ListStorageAccountKeysArgs
        {
            ResourceGroupName = resourceGroupName,
            AccountName = accountName
        });
        return accountKeys.Keys[keyIndex].Value;
    }
}
