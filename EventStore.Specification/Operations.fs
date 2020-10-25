﻿namespace EventStore

open Language
open EventStore.Core.Language

module Operations =

    type Create    = ConnectionRequest -> AsyncResult<Connection , ErrorDescription>
    type Terminate = Connection        -> AsyncResult<unit       , ErrorDescription>

    type AppendToStream           = Stream -> Event               -> AsyncResult<EventId   , ErrorDescription>
    type ReadStreamEventsBackward = Stream -> StartIndex -> Count -> AsyncResult<Event seq , ErrorDescription>