/**
 * File: AssemblyInfo.cs
 * Purpose: Exposes pure service helpers to the API test assembly without making them public.
 * Author: M.K.E Dharmarathne it23142732
 * Date: 2026
 */

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("SmartMicrogrid.API.Tests")]
