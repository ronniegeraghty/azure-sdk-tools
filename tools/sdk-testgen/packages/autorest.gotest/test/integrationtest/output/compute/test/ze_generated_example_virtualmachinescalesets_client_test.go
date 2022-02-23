//go:build go1.16
// +build go1.16

// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.
// Code generated by Microsoft (R) AutoRest Code Generator.
// Changes may cause incorrect behavior and will be lost if the code is regenerated.

package test_test

import (
	"context"
	"log"

	"time"

	"github.com/Azure/azure-sdk-for-go/sdk/azcore/to"
	"github.com/Azure/azure-sdk-for-go/sdk/azidentity"
)

// x-ms-original-file: specification/compute/resource-manager/Microsoft.Compute/stable/2021-03-01/examples/ListVirtualMachineScaleSetsInASubscriptionByLocation.json
func ExampleVirtualMachineScaleSetsClient_ListByLocation() {
	cred, err := azidentity.NewDefaultAzureCredential(nil)
	if err != nil {
		log.Fatalf("failed to obtain a credential: %v", err)
	}
	ctx := context.Background()
	client := test.NewVirtualMachineScaleSetsClient("<subscription-id>", cred, nil)
	pager := client.ListByLocation("<location>",
		nil)
	for {
		nextResult := pager.NextPage(ctx)
		if err := pager.Err(); err != nil {
			log.Fatalf("failed to advance page: %v", err)
		}
		if !nextResult {
			break
		}
		for _, v := range pager.PageResponse().Value {
			log.Printf("Pager result: %#v\n", v)
		}
	}
}

// x-ms-original-file: specification/compute/resource-manager/Microsoft.Compute/stable/2021-03-01/examples/CreateACustomImageScaleSetFromAnUnmanagedGeneralizedOsImage.json
func ExampleVirtualMachineScaleSetsClient_BeginCreateOrUpdate() {
	cred, err := azidentity.NewDefaultAzureCredential(nil)
	if err != nil {
		log.Fatalf("failed to obtain a credential: %v", err)
	}
	ctx := context.Background()
	client := test.NewVirtualMachineScaleSetsClient("<subscription-id>", cred, nil)
	poller, err := client.BeginCreateOrUpdate(ctx,
		"<resource-group-name>",
		"<vm-scale-set-name>",
		test.VirtualMachineScaleSet{
			Location: to.StringPtr("<location>"),
			Properties: &test.VirtualMachineScaleSetProperties{
				Overprovision: to.BoolPtr(true),
				UpgradePolicy: &test.UpgradePolicy{
					Mode: test.UpgradeModeManual.ToPtr(),
				},
				VirtualMachineProfile: &test.VirtualMachineScaleSetVMProfile{
					NetworkProfile: &test.VirtualMachineScaleSetNetworkProfile{
						NetworkInterfaceConfigurations: []*test.VirtualMachineScaleSetNetworkConfiguration{
							{
								Name: to.StringPtr("<name>"),
								Properties: &test.VirtualMachineScaleSetNetworkConfigurationProperties{
									EnableIPForwarding: to.BoolPtr(true),
									IPConfigurations: []*test.VirtualMachineScaleSetIPConfiguration{
										{
											Name: to.StringPtr("<name>"),
											Properties: &test.VirtualMachineScaleSetIPConfigurationProperties{
												Subnet: &test.APIEntityReference{
													ID: to.StringPtr("<id>"),
												},
											},
										}},
									Primary: to.BoolPtr(true),
								},
							}},
					},
					OSProfile: &test.VirtualMachineScaleSetOSProfile{
						AdminPassword:      to.StringPtr("<admin-password>"),
						AdminUsername:      to.StringPtr("<admin-username>"),
						ComputerNamePrefix: to.StringPtr("<computer-name-prefix>"),
					},
					StorageProfile: &test.VirtualMachineScaleSetStorageProfile{
						OSDisk: &test.VirtualMachineScaleSetOSDisk{
							Name:         to.StringPtr("<name>"),
							Caching:      test.CachingTypesReadWrite.ToPtr(),
							CreateOption: test.DiskCreateOptionTypesFromImage.ToPtr(),
							Image: &test.VirtualHardDisk{
								URI: to.StringPtr("<uri>"),
							},
						},
					},
				},
			},
		},
		nil)
	if err != nil {
		log.Fatal(err)
	}
	res, err := poller.PollUntilDone(ctx, 30*time.Second)
	if err != nil {
		log.Fatal(err)
	}
	log.Printf("Response result: %#v\n", res.VirtualMachineScaleSetsClientCreateOrUpdateResult)
}

// x-ms-original-file: specification/compute/resource-manager/Microsoft.Compute/stable/2021-03-01/examples/ForceDeleteVirtualMachineScaleSets.json
func ExampleVirtualMachineScaleSetsClient_BeginDelete() {
	cred, err := azidentity.NewDefaultAzureCredential(nil)
	if err != nil {
		log.Fatalf("failed to obtain a credential: %v", err)
	}
	ctx := context.Background()
	client := test.NewVirtualMachineScaleSetsClient("<subscription-id>", cred, nil)
	poller, err := client.BeginDelete(ctx,
		"<resource-group-name>",
		"<vm-scale-set-name>",
		&test.VirtualMachineScaleSetsClientBeginDeleteOptions{ForceDeletion: to.BoolPtr(true)})
	if err != nil {
		log.Fatal(err)
	}
	_, err = poller.PollUntilDone(ctx, 30*time.Second)
	if err != nil {
		log.Fatal(err)
	}
}

// x-ms-original-file: specification/compute/resource-manager/Microsoft.Compute/stable/2021-03-01/examples/GetVirtualMachineScaleSetAutoPlacedOnDedicatedHostGroup.json
func ExampleVirtualMachineScaleSetsClient_Get() {
	cred, err := azidentity.NewDefaultAzureCredential(nil)
	if err != nil {
		log.Fatalf("failed to obtain a credential: %v", err)
	}
	ctx := context.Background()
	client := test.NewVirtualMachineScaleSetsClient("<subscription-id>", cred, nil)
	res, err := client.Get(ctx,
		"<resource-group-name>",
		"<vm-scale-set-name>",
		&test.VirtualMachineScaleSetsClientGetOptions{Expand: nil})
	if err != nil {
		log.Fatal(err)
	}
	log.Printf("Response result: %#v\n", res.VirtualMachineScaleSetsClientGetResult)
}