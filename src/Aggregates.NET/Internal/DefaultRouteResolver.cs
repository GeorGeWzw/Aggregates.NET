﻿using Aggregates.Contracts;
using NServiceBus;
using NServiceBus.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    public class DefaultRouteResolver : IRouteResolver
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(DefaultRouteResolver));

        private IDictionary<Type, Action<Object>> _cache;

        public DefaultRouteResolver()
        {
            _cache = new Dictionary<Type, Action<Object>>();
        }

        public Action<Object> Resolve<TId>(IEventSource<TId> eventsource, Type eventType)
        {
            Action<Object> cached = null;
            if (_cache.TryGetValue(eventType, out cached))
                return cached;
            
            var handleMethod = eventsource.GetType()
                                 .GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
                                 .SingleOrDefault(
                                        m => m.Name == "Handle" &&
                                         m.GetParameters().Length == 1 &&
                                         m.GetParameters().Single().ParameterType == eventType &&
                                         m.ReturnParameter.ParameterType == typeof(void));
                                 //.Select(m => new { Method = m, MessageType = m.GetParameters().Single().ParameterType });

            if (handleMethod == null)
            {
                // If eventType is a dynamically created type, we need to map the interface, not the unknown factory type
                if( eventType.GetInterfaces().Count() == 1)
                    return Resolve<TId>(eventsource, eventType.GetInterfaces().First());

                Logger.WarnFormat("No handle method found on type '{0}' for event Type '{1}'", eventsource.GetType().Name, eventType);
                return null;
            }

            Logger.DebugFormat("Handle method found on type '{0}' for event Type '{1}'", eventsource.GetType().Name, eventType);
            return m => handleMethod.Invoke(eventsource, new[] { m });
        }
    }
}
