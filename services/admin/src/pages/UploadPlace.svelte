<script lang="ts">
	import { navigate } from "svelte-routing";
	import Main from "../components/templates/Main.svelte";
	import request from "../lib/request";
	import * as rank from "../stores/rank";

	let disabled = false;
	let errorMessage: string | undefined;
	let successMessage: string | undefined;
	let name = "";
	let description = "";
	let placeId = "";
	let creatorId = "";
	let file: File | undefined;

	rank.promise.then(() => {
		if (!rank.is("admin")) {
			errorMessage = "You must have the administrator permission to do this.";
		}
	});

	const submit = async (e: Event) => {
		e.preventDefault();
		if (disabled) return;
		errorMessage = undefined;
		successMessage = undefined;

		if (!file) {
			errorMessage = "Choose a .rbxl file first.";
			return;
		}
		if (!placeId && !name.trim()) {
			errorMessage = "Give the new place a name.";
			return;
		}

		disabled = true;
		const body = new FormData();
		body.append("name", name);
		body.append("description", description);
		if (placeId) body.append("placeId", placeId);
		if (creatorId) body.append("creatorId", creatorId);
		body.append("file", file);
		try {
			const result = await request.post("/place/upload", body);
			successMessage =
				(result.data.isNew ? "Place created. " : "Place updated. ") +
				"Place ID " +
				result.data.placeId +
				(result.data.universeId ? ", Universe ID " + result.data.universeId + "." : ".");
		} catch (err) {
			errorMessage = err?.response?.data?.errors?.[0]?.message || err?.message || "Upload failed.";
		} finally {
			disabled = false;
		}
	};
</script>

<svelte:head>
	<title>Upload Place</title>
</svelte:head>

<Main>
	<div class="row">
		<div class="col-12">
			<h1>Upload Place</h1>
			<p>Upload a .rbxl file saved from Roblox Studio. Leaving the place ID empty creates a new
				place and makes it playable straight away.</p>
			{#if errorMessage}
				<p class="err">{errorMessage}</p>
			{/if}
			{#if successMessage}
				<p class="text-success">{successMessage}</p>
			{/if}
		</div>
		<div class="col-6">
			<label for="name">Name</label>
			<input type="text" class="form-control" id="name" bind:value={name} {disabled} />
		</div>
		<div class="col-6">
			<label for="description">Description (optional)</label>
			<input type="text" class="form-control" id="description" bind:value={description} {disabled} />
		</div>
		<div class="col-6">
			<label for="placeId">Place ID (leave empty to create a new place)</label>
			<input type="text" class="form-control" id="placeId" bind:value={placeId} {disabled} />
		</div>
		<div class="col-6">
			<label for="creatorId">Owner User ID (optional, defaults to you)</label>
			<input type="text" class="form-control" id="creatorId" bind:value={creatorId} {disabled} />
		</div>
		<div class="col-12 mt-4 mb-4">
			<label for="file">.RBXL File</label>
			<input
				type="file"
				class="form-control"
				id="file"
				accept=".rbxl"
				{disabled}
				on:change={(e) => (file = e.currentTarget.files?.[0])} />
		</div>
		<div class="col-12">
			<button class="btn btn-success" {disabled} on:click={submit}>
				{placeId ? "Upload New Version" : "Create Place"}
			</button>
		</div>
	</div>
</Main>
