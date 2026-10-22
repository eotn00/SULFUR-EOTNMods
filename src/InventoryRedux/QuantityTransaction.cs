using System;

namespace LocalSulfur.UnifiedInventory
{
    internal static class QuantityTransaction
    {
        internal static bool Run(Action change,Action verify,Action rollback,Action finish,Action<Exception> recovered,Action<Exception> failed)
        {
            try { change();verify(); }
            catch(Exception error)
            {
                try { rollback(); }
                catch(Exception recoveryError) { failed(new AggregateException(error,recoveryError));return false; }
                recovered(error);return false;
            }
            // Finalization can destroy Unity objects. Never restore quantities after this boundary.
            try { finish();return true; }
            catch(Exception error) { failed(error);return false; }
        }
    }
}
