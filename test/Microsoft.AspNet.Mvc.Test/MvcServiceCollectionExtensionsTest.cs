// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.AspNet.Antiforgery;
using Microsoft.AspNet.Authorization;
using Microsoft.AspNet.Mvc.ActionConstraints;
using Microsoft.AspNet.Mvc.ApiExplorer;
using Microsoft.AspNet.Mvc.ApplicationModels;
using Microsoft.AspNet.Mvc.Core;
using Microsoft.AspNet.Mvc.Filters;
using Microsoft.AspNet.Mvc.MvcServiceCollectionExtensionsTestControllers;
using Microsoft.AspNet.Mvc.Razor;
using Microsoft.AspNet.Routing;
using Microsoft.Framework.DependencyInjection;
using Microsoft.Framework.OptionsModel;
using Moq;
using Xunit;

namespace Microsoft.AspNet.Mvc
{
    public class MvcServiceCollectionExtensionsTest
    {
        [Fact]
        public void WithControllersAsServices_AddsTypesToControllerTypeProviderAndServiceCollection()
        {
            // Arrange
            var collection = new ServiceCollection();
            var controllerTypes = new[] { typeof(ControllerTypeA).GetTypeInfo(), typeof(TypeBController).GetTypeInfo() };

            // Act
            MvcServiceCollectionExtensions.WithControllersAsServices(collection,
                                                                     controllerTypes);

            // Assert
            var services = collection.ToList();
            Assert.Equal(4, services.Count);
            Assert.Equal(typeof(ControllerTypeA), services[0].ServiceType);
            Assert.Equal(typeof(ControllerTypeA), services[0].ImplementationType);
            Assert.Equal(ServiceLifetime.Transient, services[0].Lifetime);

            Assert.Equal(typeof(TypeBController), services[1].ServiceType);
            Assert.Equal(typeof(TypeBController), services[1].ImplementationType);
            Assert.Equal(ServiceLifetime.Transient, services[1].Lifetime);

            Assert.Equal(typeof(IControllerActivator), services[2].ServiceType);
            Assert.Equal(typeof(ServiceBasedControllerActivator), services[2].ImplementationType);
            Assert.Equal(ServiceLifetime.Transient, services[2].Lifetime);

            Assert.Equal(typeof(IControllerTypeProvider), services[3].ServiceType);
            var typeProvider = Assert.IsType<FixedSetControllerTypeProvider>(services[3].ImplementationInstance);
            Assert.Equal(controllerTypes, typeProvider.ControllerTypes.OrderBy(c => c.Name));
            Assert.Equal(ServiceLifetime.Singleton, services[3].Lifetime);
        }

        [Fact]
        public void WithControllersAsServices_ScansControllersFromSpecifiedAssemblies()
        {
            // Arrange
            var collection = new ServiceCollection();
            var assemblies = new[] { GetType().Assembly };
            var controllerTypes = new[] { typeof(ControllerTypeA), typeof(TypeBController) };

            // Act
            MvcServiceCollectionExtensions.WithControllersAsServices(collection, assemblies);

            // Assert
            var services = collection.ToList();
            Assert.Equal(4, services.Count);
            Assert.Equal(typeof(ControllerTypeA), services[0].ServiceType);
            Assert.Equal(typeof(ControllerTypeA), services[0].ImplementationType);
            Assert.Equal(ServiceLifetime.Transient, services[0].Lifetime);

            Assert.Equal(typeof(TypeBController), services[1].ServiceType);
            Assert.Equal(typeof(TypeBController), services[1].ImplementationType);
            Assert.Equal(ServiceLifetime.Transient, services[1].Lifetime);


            Assert.Equal(typeof(IControllerActivator), services[2].ServiceType);
            Assert.Equal(typeof(ServiceBasedControllerActivator), services[2].ImplementationType);
            Assert.Equal(ServiceLifetime.Transient, services[2].Lifetime);

            Assert.Equal(typeof(IControllerTypeProvider), services[3].ServiceType);
            var typeProvider = Assert.IsType<FixedSetControllerTypeProvider>(services[3].ImplementationInstance);
            Assert.Equal(controllerTypes, typeProvider.ControllerTypes.OrderBy(c => c.Name));
            Assert.Equal(ServiceLifetime.Singleton, services[3].Lifetime);
        }

        // Some MVC services can be registered multiple times, for example, 'IConfigureOptions<MvcOptions>' can
        // be registered by calling 'ConfigureMvc(...)' before the call to 'AddMvc()' in which case the options
        // configuration is run in the order they were registered.
        //
        // For these kind of multi registration service types, we want to make sure that MVC will still add its
        // services if the implementation type is different.
        [Fact]
        public void MultiRegistrationServiceTypes_AreRegistered_MultipleTimes()
        {
            // Arrange
            var services = new ServiceCollection();

            // Register a mock implementation of each service, AddMvcServices should add another implemenetation.
            foreach (var serviceType in MutliRegistrationServiceTypes)
            {
                var mockType = typeof(Mock<>).MakeGenericType(serviceType.Key);
                services.Add(ServiceDescriptor.Transient(serviceType.Key, mockType));
            }

            // Act
            services.AddMvc();

            // Assert
            foreach (var serviceType in MutliRegistrationServiceTypes)
            {
                AssertServiceCountEquals(services, serviceType.Key, serviceType.Value.Length + 1);

                foreach (var implementationType in serviceType.Value)
                {
                    AssertContainsSingle(services, serviceType.Key, implementationType);
                }
            }
        }

        [Fact]
        public void SingleRegistrationServiceTypes_AreNotRegistered_MultipleTimes()
        {
            // Arrange
            var services = new ServiceCollection();

            // Register a mock implementation of each service, AddMvcServices should not replace it.
            foreach (var serviceType in SingleRegistrationServiceTypes)
            {
                var mockType = typeof(Mock<>).MakeGenericType(serviceType);
                services.Add(ServiceDescriptor.Transient(serviceType, mockType));
            }

            // Act
            services.AddMvc();

            // Assert
            foreach (var singleRegistrationType in SingleRegistrationServiceTypes)
            {
                AssertServiceCountEquals(services, singleRegistrationType, 1);
            }
        }

        [Fact]
        public void AddMvcServicesTwice_DoesNotAddDuplicates()
        {
            // Arrange
            var services = new ServiceCollection();

            // Act
            services.AddMvc();
            services.AddMvc();

            // Assert
            var singleRegistrationServiceTypes = SingleRegistrationServiceTypes;
            foreach (var service in services)
            {
                if (singleRegistrationServiceTypes.Contains(service.ServiceType))
                {
                    // 'single-registration' services should only have one implementation registered.
                    AssertServiceCountEquals(services, service.ServiceType, 1);
                }
                else if (service.ImplementationType != null && !service.ImplementationType.Assembly.FullName.Contains("Mvc"))
                {
                    // Ignore types that don't come from MVC
                }
                else
                {
                    // 'multi-registration' services should only have one *instance* of each implementation registered.
                    AssertContainsSingle(services, service.ServiceType, service.ImplementationType);
                }
            }
        }

        private IEnumerable<Type> SingleRegistrationServiceTypes
        {
            get
            {
                var services = new ServiceCollection();
                services.AddMvc();

                var multiRegistrationServiceTypes = MutliRegistrationServiceTypes;
                return services
                    .Where(sd => !multiRegistrationServiceTypes.Keys.Contains(sd.ServiceType))
                    .Where(sd => sd.ServiceType.Assembly.FullName.Contains("Mvc"))
                    .Select(sd => sd.ServiceType);
            }
        }

        private Dictionary<Type, Type[]> MutliRegistrationServiceTypes
        {
            get
            {
                return new Dictionary<Type, Type[]>()
                {
                    {
                        typeof(IConfigureOptions<MvcOptions>),
                        new Type[]
                        {
                            typeof(MvcCoreMvcOptionsSetup),
                            typeof(MvcDataAnnotationsMvcOptionsSetup),
                            typeof(MvcJsonMvcOptionsSetup),
                            typeof(TempDataMvcOptionsSetup),
                        }
                    },
                    {
                        typeof(IConfigureOptions<RouteOptions>),
                        new Type[]
                        {
                            typeof(MvcCoreRouteOptionsSetup),
                        }
                    },
                    {
                        typeof(IConfigureOptions<MvcViewOptions>),
                        new Type[]
                        {
                            typeof(MvcViewOptionsSetup),
                            typeof(MvcRazorMvcViewOptionsSetup),
                        }
                    },
                    {
                        typeof(IConfigureOptions<RazorViewEngineOptions>),
                        new Type[]
                        {
                            typeof(RazorViewEngineOptionsSetup),
                        }
                    },
                                        {
                        typeof(IActionConstraintProvider),
                        new Type[]
                        {
                            typeof(DefaultActionConstraintProvider),
                        }
                    },
                    {
                        typeof(IActionDescriptorProvider),
                        new Type[]
                        {
                            typeof(ControllerActionDescriptorProvider),
                        }
                    },
                    {
                        typeof(IActionInvokerProvider),
                        new Type[]
                        {
                            typeof(ControllerActionInvokerProvider),
                        }
                    },
                    {
                        typeof(IFilterProvider),
                        new Type[]
                        {
                            typeof(DefaultFilterProvider),
                        }
                    },
                    {
                        typeof(IControllerPropertyActivator),
                        new Type[]
                        {
                            typeof(DefaultControllerPropertyActivator),
                            typeof(ViewDataDictionaryControllerPropertyActivator),
                        }
                    },
                    {
                        typeof(IApplicationModelProvider),
                        new Type[]
                        {
                            typeof(DefaultApplicationModelProvider),
                            typeof(CorsApplicationModelProvider),
                            typeof(AuthorizationApplicationModelProvider),
                        }
                    },
                    {
                        typeof(IApiDescriptionProvider),
                        new Type[]
                        {
                            typeof(DefaultApiDescriptionProvider),
                        }
                    },
                };
            }
        }

        private void AssertServiceCountEquals(
            IServiceCollection services,
            Type serviceType,
            int expectedServiceRegistrationCount)
        {
            var serviceDescriptors = services.Where(serviceDescriptor => serviceDescriptor.ServiceType == serviceType);
            var actual = serviceDescriptors.Count();

            Assert.True(
                (expectedServiceRegistrationCount == actual),
                $"Expected service type '{serviceType}' to be registered {expectedServiceRegistrationCount}" +
                $" time(s) but was actually registered {actual} time(s).");
        }

        private void AssertContainsSingle(
            IServiceCollection services,
            Type serviceType,
            Type implementationType)
        {
            var matches = services
                .Where(sd =>
                    sd.ServiceType == serviceType &&
                    sd.ImplementationType == implementationType)
                .ToArray();

            if (matches.Length == 0)
            {
                Assert.True(
                    false,
                    $"Could not find an instance of {implementationType} registered as {serviceType}");
            }
            else if (matches.Length > 1)
            {
                Assert.True(
                    false,
                    $"Found multiple instances of {implementationType} registered as {serviceType}");
            }
        }

        private class CustomActivator : IControllerActivator
        {
            public object Create(ActionContext context, Type controllerType)
            {
                throw new NotImplementedException();
            }
        }

        public class CustomTypeProvider : IControllerTypeProvider
        {
            public IEnumerable<TypeInfo> ControllerTypes { get; set; }
        }
    }
}

// These controllers are used to test the UseControllersAsServices implementation
// which REQUIRES that they be public top-level classes. To avoid having to stub out the
// implementation of this class to test it, they are just top level classes. Don't reuse
// these outside this test - find a better way or use nested classes to keep the tests
// independent.
namespace Microsoft.AspNet.Mvc.MvcServiceCollectionExtensionsTestControllers
{
    public class ControllerTypeA : Controller
    {

    }

    public class TypeBController
    {

    }
}
