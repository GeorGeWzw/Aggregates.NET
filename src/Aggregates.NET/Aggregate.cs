using Aggregates.Contracts;
using NEventStore;
using NServiceBus;
using NServiceBus.ObjectBuilder;
using NServiceBus.ObjectBuilder.Common;
using System;
using System.Collections.Generic;

namespace Aggregates
{

    public abstract class Aggregate<TId> : IEventSource<TId>, IEventRouter, IRegisterEntities, INeedBuilder, INeedStream, INeedEventFactory, INeedRouteResolver
    {
        public TId Id
        {
            get
            {
                // Dont use unsupported Ids kids
                var converter = System.ComponentModel.TypeDescriptor.GetConverter(typeof(TId));
                if (converter != null && converter.IsValid(this.StreamId))
                    return (TId)converter.ConvertFromString(this.StreamId);
                else
                    return (TId)Activator.CreateInstance(typeof(TId));
            }
        }
        public String BucketId { get { return ""; } }// _eventStream.BucketId; } }

        String IEventSource.StreamId { get { return this.StreamId; } }
        Int32 IEventSource.Version { get { return this.Version; } }

        public String StreamId { get { return _eventStream.StreamId; } }
        public Int32 Version { get { return _eventStream.StreamRevision; } }

        IBuilder INeedBuilder.Builder { get; set; }
        IEventStream INeedStream.Stream { get; set; }
        IMessageCreator INeedEventFactory.EventFactory { get; set; }
        IRouteResolver INeedRouteResolver.Resolver { get; set; }

        // The lengths I must go to to avoid putting services into the constructor
        protected IBuilder _builder { get { return (this as INeedBuilder).Builder; } }
        private IEventStream _eventStream { get { return (this as INeedStream).Stream; } }
        private IMessageCreator _eventFactory { get { return (this as INeedEventFactory).EventFactory; } }
        private IRouteResolver _resolver { get { return (this as INeedRouteResolver).Resolver; } }

        protected IList<IEntity> _entities;

        protected Aggregate() 
        {
            _entities = new List<IEntity>();
        }


        void IEventSource.Hydrate(IEnumerable<object> events)
        {
            foreach (var @event in events)
            {
                Raise(@event);
            }
        }
        void IEventSource.Apply<TEvent>(Action<TEvent> action)
        {
            Apply(action);
        }

        protected void Apply<TEvent>(Action<TEvent> action)
        {
            var @event = _eventFactory.CreateInstance(action);

            Raise(@event);

            _eventStream.Add(new EventMessage
            {
                Body = @event,
                Headers = new Dictionary<string, object>
                {
                    { "Event", typeof(TEvent).FullName },
                    { "EventVersion", this.Version }
                    // Todo: Support user headers via method or attributes
                }
            });
        }


        private void Raise(object @event)
        {
            RouteFor(@event.GetType())(@event);
        }

        Action<Object> IEventRouter.RouteFor(Type eventType)
        {
            return RouteFor(eventType);
        }

        protected virtual Action<Object> RouteFor(Type eventType)
        {
            var route = _resolver.Resolve(this, eventType);
            if( route == null )
                throw new HandlerNotFoundException(String.Format("No handler for event {0}", eventType.Name));

            return e => route(e);
        }




        void IRegisterEntities.RegisterChild(IEntity entity)
        {
            entity.RegisterEventStream(this._eventStream);
            _entities.Add(entity);
        }
    }

    public abstract class AggregateWithMemento<TId, TMemento> : Aggregate<TId>, ISnapshotting where TMemento : class, IMemento
    {
        void ISnapshotting.RestoreSnapshot(ISnapshot snapshot)
        {
            var memento = (TMemento)snapshot.Payload;

            RestoreSnapshot(memento);
        }

        ISnapshot ISnapshotting.TakeSnapshot()
        {
            var memento = TakeSnapshot();
            return new Snapshot(this.BucketId, this.StreamId, this.Version, memento);
        }

        Boolean ISnapshotting.ShouldTakeSnapshot(Int32 CurrentVersion, Int32 CommitVersion)
        {
            return ShouldTakeSnapshot(CurrentVersion, CommitVersion);
        }

        protected abstract void RestoreSnapshot(TMemento memento);

        protected abstract TMemento TakeSnapshot();

        protected virtual Boolean ShouldTakeSnapshot(Int32 CurrentVersion, Int32 CommitVersion) { return false; }
    }
}